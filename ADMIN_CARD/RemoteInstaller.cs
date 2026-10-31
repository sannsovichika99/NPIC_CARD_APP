using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NPIC_CARD_APP.Services;

namespace CardStudio.Admin
{
    /// <summary>
    /// An administrator account of the target PCs, kept in memory for the open install window only (never saved).
    /// Empty <see cref="User"/> = the Windows account ADMIN_CARD runs as (a domain administrator).
    /// </summary>
    internal sealed class RemoteCredentials
    {
        public string User;
        public string Password;
        public bool UseMine { get { return string.IsNullOrWhiteSpace(User); } }
    }

    /// <summary>What <see cref="RemoteInstaller.Check"/> found on a PC.</summary>
    internal sealed class RemoteInfo
    {
        public string Computer;
        public string Os;
        /// <summary>C:\Windows on the PC (ADMIN$ is this folder).</summary>
        public string WindowsDir;
        /// <summary>"C:"</summary>
        public string SystemDrive;
        /// <summary>Card Studio's Machine ID of the PC (from its MachineGuid), null when it could not be read.</summary>
        public string MachineId;
    }

    /// <summary>Remote install failed, with a message for the administrator.</summary>
    /// <summary>Why a PC could not be used: decides which help the install window shows.</summary>
    internal enum RemoteProblem
    {
        Other,
        /// <summary>The account was refused (not an administrator, or Windows' remote UAC for local / Microsoft accounts).</summary>
        AccessDenied,
        /// <summary>Wrong user name or password.</summary>
        WrongPassword,
        /// <summary>File sharing or WMI does not answer: firewall, Public network, or the PC is off.</summary>
        Unreachable
    }

    /// <summary>Remote install failed, with a message for the administrator.</summary>
    internal sealed class RemoteException : Exception
    {
        public RemoteProblem Problem { get; private set; }

        public RemoteException(string message, RemoteProblem problem = RemoteProblem.Other) : base(message)
        {
            Problem = problem;
        }
    }

    /// <summary>
    /// Installs Card Studio on another Windows PC the way IT deployment tools do, with an administrator account of that PC:
    /// <list type="number">
    /// <item>WMI (root\cimv2) reads Windows and its folder, the registry provider reads MachineGuid → the Machine ID;</item>
    /// <item>the setup and a small .cmd are copied to the PC's ADMIN$\Temp (C:\Windows\Temp);</item>
    /// <item>WMI Win32_Process.Create runs the .cmd, which runs <c>CardStudio-Setup.exe /S</c> and writes its exit code;</item>
    /// <item>the exit code (and the setup log on failure) is read back, the copied files are deleted;</item>
    /// <item>optionally the serial is written to C:\ProgramData\NPIC_CARD_APP\license.txt, where Card Studio looks first.</item>
    /// </list>
    /// The PC needs: "File and Printer Sharing" and "Windows Management Instrumentation (WMI)" allowed in its firewall (a domain
    /// usually allows them by Group Policy), and an administrator account. Local (non-domain) accounts other than the built-in
    /// Administrator are filtered by UAC over the network unless LocalAccountTokenFilterPolicy is set on that PC.
    /// </summary>
    internal static class RemoteInstaller
    {
        public const string SetupName = "CardStudio-Setup.exe";
        private const string ScriptName = "CardStudio-Install.cmd";
        private const string ResultName = "CardStudio-Install.result";
        private const string LogName = "CardStudio-Setup.log";
        private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(10);

        // ===================== Check =====================

        /// <summary>Connects and reads the PC's Windows, folders and Machine ID. Throws <see cref="RemoteException"/>.</summary>
        public static RemoteInfo Check(string host, RemoteCredentials cred)
        {
            try
            {
                var info = new RemoteInfo();
                ManagementScope scope = Scope(host, cred, @"root\cimv2");
                using (var searcher = new ManagementObjectSearcher(scope,
                           new ObjectQuery("SELECT CSName, Caption, Version, WindowsDirectory, SystemDrive FROM Win32_OperatingSystem")))
                using (ManagementObjectCollection items = searcher.Get())
                    foreach (ManagementObject os in items.Cast<ManagementObject>())
                        using (os)
                        {
                            info.Computer = (os["CSName"] as string ?? host).ToUpperInvariant();
                            info.Os = ((os["Caption"] as string ?? "Windows").Replace("Microsoft ", "") + " (" + os["Version"] + ")").Trim();
                            info.WindowsDir = os["WindowsDirectory"] as string;
                            info.SystemDrive = os["SystemDrive"] as string;
                        }
                if (string.IsNullOrEmpty(info.WindowsDir)) throw new RemoteException("The PC did not say where Windows is installed.");

                string guid = ReadMachineGuid(host, cred);
                info.MachineId = string.IsNullOrEmpty(guid) ? null : LicenseService.MachineIdFrom(guid.Trim().ToLowerInvariant());

                using (Share(host, "ADMIN$", cred))
                    if (!Directory.Exists(AdminTemp(host)))
                        throw new RemoteException("The PC's admin share (ADMIN$) is not reachable: turn on File and Printer Sharing there.");
                return info;
            }
            catch (Exception ex) when (!(ex is RemoteException))
            {
                throw Wrap(ex);
            }
        }

        private static string ReadMachineGuid(string host, RemoteCredentials cred)
        {
            // The 64-bit registry view: a 32-bit WMI provider would read a different (redirected) key on 64-bit Windows.
            var context = new ManagementNamedValueCollection { { "__ProviderArchitecture", 64 }, { "__RequiredArchitecture", true } };
            ManagementScope scope = Scope(host, cred, @"root\default");
            using (var reg = new ManagementClass(scope, new ManagementPath("StdRegProv"), new ObjectGetOptions(context)))
            using (ManagementBaseObject input = reg.GetMethodParameters("GetStringValue"))
            {
                input["hDefKey"] = 0x80000002u;   // HKEY_LOCAL_MACHINE
                input["sSubKeyName"] = @"SOFTWARE\Microsoft\Cryptography";
                input["sValueName"] = "MachineGuid";
                using (ManagementBaseObject output = reg.InvokeMethod("GetStringValue", input, new InvokeMethodOptions(context, TimeSpan.FromSeconds(30))))
                    return output == null ? null : output["sValue"] as string;
            }
        }

        // ===================== Install =====================

        /// <summary>
        /// Copies <paramref name="setupPath"/> to the PC, runs it silently and returns null when it worked, otherwise why not.
        /// <paramref name="status"/> gets short progress lines. Throws <see cref="RemoteException"/> when the PC cannot be used.
        /// </summary>
        public static string Install(string host, RemoteCredentials cred, RemoteInfo info, string setupPath, bool desktopShortcut,
                                     Action<string> status, CancellationToken cancel)
        {
            string shareTemp = AdminTemp(host);
            string remoteTemp = info.WindowsDir.TrimEnd('\\') + @"\Temp";
            try
            {
                using (Share(host, "ADMIN$", cred))
                {
                    status("Copying the setup…");
                    Directory.CreateDirectory(shareTemp);
                    string result = Path.Combine(shareTemp, ResultName);
                    TryDelete(result);
                    TryDelete(Path.Combine(shareTemp, LogName));
                    File.Copy(setupPath, Path.Combine(shareTemp, SetupName), true);
                    File.WriteAllText(Path.Combine(shareTemp, ScriptName), Script(desktopShortcut), Encoding.ASCII);

                    status("Running the setup…");
                    ManagementScope scope = Scope(host, cred, @"root\cimv2");
                    using (var process = new ManagementClass(scope, new ManagementPath("Win32_Process"), null))
                    using (ManagementBaseObject input = process.GetMethodParameters("Create"))
                    {
                        input["CommandLine"] = ScriptCommand(remoteTemp);
                        input["CurrentDirectory"] = remoteTemp;
                        using (ManagementBaseObject output = process.InvokeMethod("Create", input, null))
                        {
                            uint code = Convert.ToUInt32(output["ReturnValue"]);
                            if (code != 0) throw new RemoteException("Windows did not start the setup on the PC (" + CreateError(code) + ").");
                        }
                    }

                    // ---- wait for the exit code ----
                    DateTime until = DateTime.UtcNow + SetupTimeout;
                    string text = null;
                    while (DateTime.UtcNow < until)
                    {
                        if (cancel.WaitHandle.WaitOne(2000)) return "Stopped waiting: the setup may still finish on the PC.";
                        try { if (File.Exists(result)) text = File.ReadAllText(result).Trim(); }
                        catch (IOException) { }   // still being written
                        if (!string.IsNullOrEmpty(text)) break;
                    }
                    if (string.IsNullOrEmpty(text)) return "The setup did not finish within " + SetupTimeout.TotalMinutes + " minutes.";

                    int exit;
                    string why = int.TryParse(text, out exit) ? SetupError(exit) : "The setup ended with \"" + text + "\".";
                    if (why != null && exit == 1) why += LastLogLines(Path.Combine(shareTemp, LogName));

                    TryDelete(Path.Combine(shareTemp, SetupName));
                    TryDelete(Path.Combine(shareTemp, ScriptName));
                    TryDelete(result);
                    if (why == null) TryDelete(Path.Combine(shareTemp, LogName));
                    return why;
                }
            }
            catch (Exception ex) when (!(ex is RemoteException))
            {
                throw Wrap(ex);
            }
        }

        /// <summary>
        /// The .cmd run on the PC, next to the setup: runs it silently and writes its exit code to <see cref="ResultName"/>.
        /// %~dp0 = the folder of the .cmd; TEMP points there too, so the setup log can be read back on failure.
        /// </summary>
        internal static string Script(bool desktopShortcut)
        {
            return "@echo off\r\n" +
                   "set \"TEMP=%~dp0\"\r\nset \"TMP=%~dp0\"\r\n" +
                   "\"%~dp0" + SetupName + "\" /S" + (desktopShortcut ? "" : " /NoDesktop") + "\r\n" +
                   // Redirect first: in "echo 5>file" cmd takes the 5 for a handle number and writes nothing.
                   ">\"%~dp0" + ResultName + "\" echo %errorlevel%\r\n";
        }

        /// <summary>The command line Win32_Process.Create gets to run the script in <paramref name="folder"/>.</summary>
        internal static string ScriptCommand(string folder)
        {
            return "cmd.exe /c \"\"" + folder.TrimEnd('\\') + "\\" + ScriptName + "\"\"";
        }

        /// <summary>Saves <paramref name="serial"/> (already checked for the PC's Machine ID) where Card Studio reads it first.</summary>
        public static void WriteSerial(string host, RemoteCredentials cred, RemoteInfo info, string serial)
        {
            string drive = string.IsNullOrEmpty(info.SystemDrive) ? "C" : info.SystemDrive.TrimEnd(':', '\\');
            try
            {
                using (Share(host, drive + "$", cred))
                {
                    string folder = @"\\" + host + @"\" + drive + @"$\ProgramData\NPIC_CARD_APP";
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "license.txt"),
                        SerialFormat.Group(SerialFormat.Normalize(serial), 5) + "\r\n", Encoding.ASCII);
                }
            }
            catch (Exception ex) when (!(ex is RemoteException))
            {
                RemoteException why = Wrap(ex);
                throw new RemoteException("Installed, but the serial could not be saved: " + why.Message, why.Problem);
            }
        }

        // ===================== Messages =====================

        private static string SetupError(int exit)
        {
            switch (exit)
            {
                case 0: return null;
                case 1: return "The setup failed on the PC.";
                case 2: return "The setup refused its options (is it an older CardStudio-Setup.exe?).";
                case 3: return "The setup was cancelled.";
                case 4: return ".NET Framework 4.8.1 is missing on the PC. Install it there first (Windows Update, or dotnet.microsoft.com), then try again.";
                case 5: return "Card Studio is open on the PC. Close it there, then try again.";
                case 7: return "Another Card Studio setup is already running on the PC.";
                case 740: return "The account is not an administrator of the PC (or UAC filters it over the network).";
                default: return "The setup ended with code " + exit + ".";
            }
        }

        private static string CreateError(uint code)
        {
            switch (code)
            {
                case 2: return "access denied";
                case 3: return "the account lacks a privilege";
                case 9: return "path not found";
                case 21: return "invalid parameter";
                default: return "code " + code;
            }
        }

        /// <summary>The administrator's version of what went wrong (firewall, account, name…), with its kind.</summary>
        public static RemoteException Wrap(Exception ex)
        {
            var com = ex as COMException;
            uint hr = com != null ? (uint)com.ErrorCode : 0;
            var w32 = ex as Win32Exception;
            int win32 = w32 != null ? w32.NativeErrorCode : 0;

            if (ex is UnauthorizedAccessException || hr == 0x80070005 || win32 == 5)
                return new RemoteException(
                    "Access denied: Windows on that PC refused remote administration for this account. Use an administrator " +
                    "account of that PC, and run the preparation script on it once (Save Preparation Script…): Windows blocks " +
                    "local and Microsoft accounts over the network until it has run.", RemoteProblem.AccessDenied);
            if (hr == 0x8007052E || win32 == 86 || win32 == 1326)
                return new RemoteException("Wrong user name or password for that PC. A Microsoft account signs in with its " +
                                           "e-mail address and its password (not the PIN).", RemoteProblem.WrongPassword);
            if (hr == 0x800706BA || hr == 0x800706BE || hr == 0x80070776)
                return new RemoteException("The PC does not answer remote management (WMI): its firewall blocks it, its network is " +
                                           "set to Public, or the PC is off. Run the preparation script on it once.", RemoteProblem.Unreachable);
            if (win32 == 53 || win32 == 67 || win32 == 51)
                return new RemoteException("The PC's file sharing does not answer: its firewall blocks it or its network is set to " +
                                           "Public. Run the preparation script on it once.", RemoteProblem.Unreachable);
            if (win32 == 1219)
                return new RemoteException("Windows is already connected to that PC with another account (an open Explorer window " +
                                           "or mapped drive). Close it, or use the same account.");
            if (win32 == 1327 || win32 == 1331)
                return new RemoteException("Windows on that PC refuses this account over the network (a blank password, a disabled " +
                                           "account or a sign-in restriction). Use an administrator account with a password.", RemoteProblem.AccessDenied);
            var mex = ex as ManagementException;
            if (mex != null && mex.ErrorCode == ManagementStatus.LocalCredentials)
                return new RemoteException("This is the PC ADMIN_CARD runs on: run CardStudio-Setup.exe here instead.");
            if (mex != null && mex.ErrorCode == ManagementStatus.AccessDenied)
                return Wrap(new UnauthorizedAccessException());
            if (w32 != null) return new RemoteException(w32.Message);
            if (ex is TimeoutException) return new RemoteException("The PC took too long to answer.", RemoteProblem.Unreachable);
            return new RemoteException(ex.Message.Split('\n')[0].Trim());
        }

        /// <summary>
        /// "admin" → "PC-NAME\admin": a user name without a computer or domain would be checked against this PC's accounts (or
        /// the domain of this PC), not the target's. Names with \ or @ (DOMAIN\user, a Microsoft account e-mail) stay as typed.
        /// </summary>
        public static string QualifyUser(string user, string computer)
        {
            user = (user ?? "").Trim();
            if (user.Length == 0 || user.Contains("\\") || user.Contains("@") || string.IsNullOrEmpty(computer)) return user;
            return computer.Trim().ToUpperInvariant() + "\\" + user;
        }

        // ===================== Preparation script =====================

        public const string PreparationScriptName = "Prepare-CardStudio-Install.cmd";

        /// <summary>
        /// The one-time script for each target PC (run as administrator there), for networks without a domain:
        /// <list type="bullet">
        /// <item>LocalAccountTokenFilterPolicy = 1: local and Microsoft administrator accounts keep their rights over the network
        /// (Windows strips them by default, which is the "Access denied");</item>
        /// <item>firewall: Windows' File and Printer Sharing rules for Private and Domain networks, and Card Studio Admin's own WMI
        /// rules for Private and Domain (Windows' WMI rule for private networks also opens Public ones);</item>
        /// <item>optionally sets the PC's networks to Private (on a trusted school or office network only).</item>
        /// </list>
        /// Language-neutral group names (@FirewallAPI.dll,-28502 = File and Printer Sharing), so it works on Khmer Windows too.
        /// </summary>
        public static string PreparationScript()
        {
            return string.Join("\r\n", new[]
            {
                "@echo off",
                "rem Card Studio Admin: allow installing Card Studio on this PC over the network. Run once, as administrator.",
                "rem Undo: reg delete \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v LocalAccountTokenFilterPolicy /f",
                "rem       and in PowerShell: Remove-NetFirewallRule -Name 'CardStudioAdmin-*'",
                "title Prepare this PC for Card Studio Admin",
                "net session >nul 2>&1",
                "if errorlevel 1 (",
                "  echo This script must run as administrator: right-click it and choose \"Run as administrator\".",
                "  pause",
                "  exit /b 1",
                ")",
                "echo.",
                "echo [1/3] Remote administration for local and Microsoft accounts",
                "reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f >nul",
                "echo.",
                "echo [2/3] Windows Firewall: file sharing and remote management (WMI) on private and domain networks only",
                "powershell -NoProfile -ExecutionPolicy Bypass -Command \"" +
                    "Get-NetFirewallRule -Group '@FirewallAPI.dll,-28502' | Where-Object { $_.Direction -eq 'Inbound' -and ($_.Profile -eq 'Private' -or $_.Profile -eq 'Domain') } | Enable-NetFirewallRule; " +
                    "Remove-NetFirewallRule -Name 'CardStudioAdmin-*' -ErrorAction SilentlyContinue; " +
                    "New-NetFirewallRule -Name 'CardStudioAdmin-DCOM' -DisplayName 'Card Studio Admin: WMI (DCOM-In)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 135 -Program '%SystemRoot%\\system32\\svchost.exe' -Service RpcSs -Profile Domain,Private | Out-Null; " +
                    "New-NetFirewallRule -Name 'CardStudioAdmin-WMI' -DisplayName 'Card Studio Admin: WMI (WMI-In)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort RPC -Program '%SystemRoot%\\system32\\svchost.exe' -Service Winmgmt -Profile Domain,Private | Out-Null\"",
                "echo.",
                "echo [3/3] Network type: the rules above only work on a Private (or domain) network",
                "powershell -NoProfile -ExecutionPolicy Bypass -Command \"Get-NetConnectionProfile | Format-Table Name, InterfaceAlias, NetworkCategory -AutoSize\"",
                "choice /C YN /M \"Set Public networks of this PC to Private (only on a trusted school or office network)\"",
                "if errorlevel 2 goto done",
                "powershell -NoProfile -ExecutionPolicy Bypass -Command \"Get-NetConnectionProfile | Where-Object { $_.NetworkCategory -eq 'Public' } | Set-NetConnectionProfile -NetworkCategory Private\"",
                ":done",
                "echo.",
                "echo Done. In Card Studio Admin, use an administrator account of this PC:",
                "echo   %COMPUTERNAME%\\user name   (or a Microsoft account e-mail address, with its password - not the PIN)",
                "echo.",
                "pause",
                ""
            });
        }

        private static string LastLogLines(string log)
        {
            try
            {
                if (!File.Exists(log)) return "";
                string[] lines = File.ReadAllLines(log);
                return "\nSetup log: " + string.Join(" / ", lines.Skip(Math.Max(0, lines.Length - 3)).Select(l => l.Trim()));
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
        }

        // ===================== Plumbing =====================

        private static ManagementScope Scope(string host, RemoteCredentials cred, string ns)
        {
            var options = new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy,
                EnablePrivileges = true,
                Timeout = TimeSpan.FromSeconds(30)
            };
            if (!cred.UseMine)
            {
                options.Username = cred.User.Trim();
                options.Password = cred.Password ?? "";
            }
            var scope = new ManagementScope(@"\\" + host + @"\" + ns, options);
            scope.Connect();
            return scope;
        }

        private static string AdminTemp(string host)
        {
            return @"\\" + host + @"\ADMIN$\Temp";
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // ---- SMB connection with the administrator's account (none needed for "my Windows account") ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope, dwType, dwDisplayType, dwUsage;
            public string lpLocalName, lpRemoteName, lpComment, lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetAddConnection2(ref NETRESOURCE resource, string password, string user, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);

        private sealed class Connection : IDisposable
        {
            private readonly string _unc;
            public Connection(string unc) { _unc = unc; }
            public void Dispose() { if (_unc != null) WNetCancelConnection2(_unc, 0, true); }
        }

        /// <summary>Opens \\host\share with the account; disposing closes it. Throws Win32Exception.</summary>
        private static IDisposable Share(string host, string share, RemoteCredentials cred)
        {
            if (cred.UseMine) return new Connection(null);
            string unc = @"\\" + host + @"\" + share;
            var nr = new NETRESOURCE { dwType = 1 /* disk */, lpRemoteName = unc };
            int err = WNetAddConnection2(ref nr, cred.Password ?? "", cred.User.Trim(), 0);
            if (err == 0) return new Connection(unc);
            if (err == 1219 || err == 85) return new Connection(null);   // already connected (same session): use it as it is
            throw new Win32Exception(err);
        }
    }
}
