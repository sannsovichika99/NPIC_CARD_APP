# Card Studio Installer

A custom C# (.NET Framework, WinForms) installer for `NPIC_CARD_APP`. It builds to **one file**,
`dist\CardStudio-Setup.exe`, that contains the whole app and that:

- installs to `C:\Program Files\Card Studio` (or a folder the user picks)
- creates a **desktop shortcut** and a **Start Menu shortcut** (each optional)
- registers an **uninstaller** in *Settings > Apps > Installed apps* and adds an *Uninstall Card Studio* item to the Start Menu
- checks for .NET Framework 4.8.1, refuses to run while Card Studio is open, rolls back a failed first install
- keeps the user's card images (`Assets\`) when upgrading, and lets non-admin staff replace them
- supports silent install and uninstall for deployment scripts

No third-party tools are needed (no WiX, Inno Setup or NSIS), only Visual Studio or the Build Tools.

---

## 1. Requirements

| To build | To install (target PC) |
|---|---|
| Visual Studio 2022 or newer (or *Build Tools for Visual Studio*) with the **.NET desktop build tools** workload | Windows 10 or 11 |
| .NET Framework **4.8.1** and **4.8** targeting packs | .NET Framework **4.8.1** (built into Windows 11 22H2+; [download](https://dotnet.microsoft.com/download/dotnet-framework/net481) for Windows 10) |
| | An administrator account (UAC prompt) |

> **Why the setup targets 4.8 while the app targets 4.8.1.** Windows 10 ships with 4.8. If the setup also
> required 4.8.1, Windows would show its own generic "this app needs .NET" error on those PCs. Built for 4.8,
> the setup starts, detects that 4.8.1 is missing (registry `Release` ≥ 533320), and offers the download page.

---

## 2. Project layout

```
NPIC_CARD_APP.sln                    ← now contains both projects
Version.cs                           ← THE version number, compiled into the app and the setup
NPIC_CARD_APP\                       ← the WinForms app (Check for Updates: UpdateForm.cs, Services\UpdateService.cs)
Installer\
  build.ps1                          ← one-command build
  README.md                          ← this guide
  CardStudio.Setup\
    CardStudio.Setup.csproj          ← builds the app, zips its output, embeds the zip
    app.manifest                     ← requireAdministrator + DPI awareness
    ProductInfo.cs                   ← name, paths, registry key (edit here)
    SetupOptions.cs                  ← command-line parsing
    Program.cs                       ← entry point: install or /uninstall, exit codes
    InstallEngine.cs                 ← extract files, ACL, shortcuts, registry, rollback, log
    UninstallEngine.cs               ← remove files/shortcuts/registry, self-delete
    Shortcut.cs                      ← .lnk creation via the Shell's IShellLink COM interface
    SetupUi.cs                       ← shared header/footer/colours
    SetupForm.cs                     ← install wizard (Options → Progress → Finished)
    UninstallForm.cs                 ← uninstall dialog (Confirm → Progress → Finished)
  build-serial-generator.ps1         ← builds the Serial Generator's setup (admin tool, see section 11)
  SerialGenerator.Setup\
    SerialGenerator.Setup.csproj     ← same engine files as CardStudio.Setup, generator payload, never the private key
    ProductInfo.cs                   ← the generator's name, paths, texts (replaces CardStudio.Setup\ProductInfo.cs)
Tools\SerialGenerator\               ← the Serial Generator (GeneratorVersion.cs = its own version number)
dist\
  CardStudio-Setup.exe               ← build output (git-ignored)
  update.json                        ← version + SHA-256 + notes, read by the app's Check for Updates
  admin\SerialGenerator-Setup.exe    ← Serial Generator setup: NOT for the Card Studio release
```

Both setups share one engine: `SerialGenerator.Setup` compiles every `.cs` file of `CardStudio.Setup` except
`ProductInfo.cs`. Anything that differs between the two products (name, paths, shortcut text, fonts, the Assets folder,
uninstall texts) belongs in `ProductInfo.cs`. Keep the members of the two `ProductInfo` classes the same.

---

## 3. How it works

### Build time

1. `CardStudio.Setup.csproj` has a `ProjectReference` to `NPIC_CARD_APP.csproj` with
   `ReferenceOutputAssembly=false`. This makes MSBuild build the app first, in the same configuration,
   without linking to it.
2. The `BuildPayload` target runs right after project references are resolved. It:
   - copies `NPIC_CARD_APP\bin\Release\**` (excluding `*.pdb`) to `obj\Release\payload\`
   - zips that folder to `obj\Release\payload.zip` with MSBuild's built-in `ZipDirectory` task
   - adds the zip as an embedded resource named `CardStudio.Setup.payload.zip`
3. The `CopyToDist` target copies the finished `CardStudio-Setup.exe` to `dist\` (Release only).

Anything added to the app with `CopyToOutputDirectory` (more PNGs, a DLL, a NuGet package) is picked up
automatically. No installer change is needed.

### Install (`CardStudio-Setup.exe`)

| Step | Detail |
|---|---|
| Prerequisite check | .NET Framework 4.8.1 present, Card Studio not running, only one setup running at a time |
| Upgrade detection | Reads `InstallLocation` from the uninstall registry key; an upgrade reuses that folder |
| Permissions | Grants `BUILTIN\Users` **Modify** on `<install>\Assets` (the app reads card images from there) |
| Files | Extracts the payload. On upgrade, existing files in `Assets\` are **not** overwritten, and files that the old version shipped but the new one does not are deleted |
| Manifest | Writes `install.manifest` (one relative path per line), used by later upgrades and by the uninstaller |
| Uninstaller | Copies the setup exe itself to `<install>\uninstall.exe` |
| Shortcuts | `C:\Users\Public\Desktop\Card Studio.lnk`; Start Menu → `Card Studio\Card Studio.lnk` and `Card Studio\Uninstall Card Studio.lnk` (all users) |
| Registry | `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\CardStudio` (64-bit view): DisplayName, DisplayVersion, Publisher, DisplayIcon, InstallLocation, UninstallString, QuietUninstallString, InstallDate, EstimatedSize, NoModify, NoRepair |
| Failure | A failed **first** install removes everything it created. A failed upgrade leaves the previous files in place |
| Launch | "Start Card Studio now" launches through `explorer.exe`, so the app runs as the signed-in user, **not** elevated |

### Uninstall (`uninstall.exe /uninstall`)

1. Removes the desktop and Start Menu shortcuts.
2. Deletes every file listed in `install.manifest`, then empty sub-folders. It never touches files outside the install folder.
3. Deletes the registry key, so the app disappears from *Settings > Apps*.
4. Optional (checkbox, or `/purge`): deletes `%AppData%\NPIC_CARD_APP` (settings.xml, print-history.csv,
   rfid-cards.csv, uploaded logos and signatures). This is **off by default**, so a reinstall keeps the user's setup.
5. A running exe cannot delete itself, so it starts a hidden `cmd.exe` that waits about 2 seconds, deletes
   `uninstall.exe`, and removes the install folder **only if it is empty** (`rmdir` without `/s`).
   Files the user added to the folder are never deleted.

---

## 4. Build

### Option A: one command (recommended)

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File Installer\build.ps1
```

With a release version (stamped into `Version.cs`, so both the app and Settings > Apps show it) and the
release notes the app's **Check for Updates** shows under "What's new":

```powershell
powershell -ExecutionPolicy Bypass -File Installer\build.ps1 -Version 1.1.0 -Notes "Faster printing.`nNew RFID page."
powershell -ExecutionPolicy Bypass -File Installer\build.ps1 -Version 1.1.0 -NotesFile release-notes.txt
```

The script finds MSBuild with `vswhere`, rebuilds the app and the setup in Release, writes `dist\update.json`,
and prints the result:

```
Installer : O:\Claude\NPIC_CARD_APP\dist\CardStudio-Setup.exe
Size      : 1.4 MB
SHA-256   : C1A7D042…
Update    : O:\Claude\NPIC_CARD_APP\dist\update.json
```

### Option B: Visual Studio

1. Open `NPIC_CARD_APP.sln`. It now shows **NPIC_CARD_APP** and **CardStudio.Setup**.
2. Select the **Release** configuration.
3. Right-click **CardStudio.Setup** and choose **Rebuild**.
4. Pick up `dist\CardStudio-Setup.exe`.

(Keep **NPIC_CARD_APP** as the startup project for day-to-day debugging. Running the setup from VS needs VS to be started as administrator.)

### Option C: plain MSBuild

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  Installer\CardStudio.Setup\CardStudio.Setup.csproj /t:Rebuild /p:Configuration=Release /p:Platform=AnyCPU
```

---

## 5. Output files

| File | What it is | Ship it? |
|---|---|---|
| `dist\CardStudio-Setup.exe` | Single-file installer (~1.4 MB, app embedded) | **Yes, the only file to distribute** |
| `Installer\CardStudio.Setup\bin\Release\CardStudio-Setup.exe` | Same exe, before the copy to `dist\` | No |
| `Installer\CardStudio.Setup\obj\Release\payload.zip` | The zipped app embedded in the setup | No (intermediate) |
| `NPIC_CARD_APP\bin\Release\*` | The app build the payload is made from | No |

What ends up on the target PC:

```
C:\Program Files\Card Studio\
  NPIC_CARD_APP.exe
  NPIC_CARD_APP.exe.config
  students-sample.csv
  Assets\  (director_signature, facebook, ministry_of_labour_logo, npic_png,
            qrcode_for_back_card, stamp, telegram, tiktok, youtube .png)
  install.manifest
  uninstall.exe
C:\Users\Public\Desktop\Card Studio.lnk
C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Card Studio\Card Studio.lnk
C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Card Studio\Uninstall Card Studio.lnk
HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\CardStudio
%TEMP%\CardStudio-Setup.log         (install/uninstall log)
```

---

## 6. Command line

```text
CardStudio-Setup.exe                          interactive install
CardStudio-Setup.exe /S                       silent install, both shortcuts, default folder
CardStudio-Setup.exe /S /D=D:\Apps\Card Studio /NoDesktop
                                              silent, custom folder, Start Menu shortcut only
uninstall.exe /uninstall                      interactive uninstall (what Settings > Apps runs)
uninstall.exe /uninstall /S                   silent uninstall, keeps %AppData% data
uninstall.exe /uninstall /S /purge            silent uninstall, also deletes %AppData%\NPIC_CARD_APP
CardStudio-Setup.exe /WaitForApp              wait up to 30 s for Card Studio to close first
                                              (how the app's Check for Updates starts the setup)
```

Run silent commands from an elevated prompt. Exit codes:

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | Failed (see `%TEMP%\CardStudio-Setup.log`) |
| 2 | Unknown argument |
| 3 | Cancelled by the user |
| 4 | .NET Framework 4.8.1 missing |
| 5 | Card Studio is running |
| 6 | Not installed (uninstall) |
| 7 | Another setup is already running |

---

## 7. Test checklist (on a clean PC or VM)

1. **Fresh install.** Run `CardStudio-Setup.exe` and accept UAC → **Install** → **Finish** with "Start Card Studio now" ticked.
   The app opens, and Task Manager → Details → *Elevated* shows **No**.
2. **Shortcuts.** The desktop icon and the Start Menu entry *Card Studio* both start the app, with the right icon.
3. **Settings > Apps.** *Card Studio* is listed with version 1.0.0, publisher NPIC and a size.
4. **Assets permission.** As a standard user, replace `C:\Program Files\Card Studio\Assets\stamp.png`. It works without a UAC prompt.
5. **Upgrade.** Build with `-Version 1.0.1` and run the new setup. The button reads **Update**, the folder is locked,
   the replaced `stamp.png` is still yours, and Settings > Apps shows 1.0.1.
6. **App running.** With Card Studio open, run the setup. It asks you to close the app (Retry/Cancel).
7. **Uninstall.** Settings > Apps → Card Studio → Uninstall. The shortcuts, registry entry and program folder are gone, and `%AppData%\NPIC_CARD_APP` is still there.
8. **Uninstall with purge.** Reinstall, then uninstall with the checkbox ticked. `%AppData%\NPIC_CARD_APP` is gone.
9. **Silent.** Run `CardStudio-Setup.exe /S` then `"C:\Program Files\Card Studio\uninstall.exe" /uninstall /S` from an
   elevated prompt. Both return exit code 0 (`echo %errorlevel%`).

---

## 8. Releasing a new version

1. Change the app as usual.
2. Run `Installer\build.ps1 -Version X.Y.Z` (or a 4-part `X.Y.Z.W`, e.g. `1.0.0.1`; or edit `AssemblyVersion` in `Version.cs`).
3. Commit and push the version bump.
4. **Publish it as a GitHub release** on https://github.com/sannsovichika99/NPIC_CARD_APP/releases:
   - tag **`vX.Y.Z`**, the same number as in `Version.cs` (the app reads the version from the tag)
   - attach **`dist\CardStudio-Setup.exe`** (keep that file name; it must contain "Setup" and end in `.exe`)
   - write the changes in the description: the app shows them under "What's new"
   - publish it as a normal release, not a draft or pre-release (those are ignored)

   ```powershell
   gh release create vX.Y.Z dist\CardStudio-Setup.exe --title "Card Studio X.Y.Z" --notes "What changed…"
   ```

   Every PC then finds it with **Check for Updates**, or by itself at the next start. You can still hand out the setup by
   hand: users run it over the old version, and no uninstall is needed.

To rename the product, publisher, install folder or registry key, edit only `ProductInfo.cs`.

### Check for Updates (in the app)

*Settings & Help… → Check for Updates*, or **Ctrl+U**. There is nothing to set up: the app always reads the **latest GitHub
release** of this repository (`UpdateService.Source` in `NPIC_CARD_APP\Services\UpdateService.cs`). It compares the
release tag with its own version (`Version.cs`), downloads the attached setup to `%TEMP%\CardStudio-Update`, checks it
against the SHA-256 digest GitHub publishes for the file, starts `CardStudio-Setup.exe /WaitForApp` (UAC prompt) and
closes. The setup upgrades in place as usual. The app also checks quietly at start-up, at most once a day (it can be
switched off). It opens the page only when there is a version the user did not skip, and the main window's title shows
"Update X.Y.Z available" until it is installed.

The repository must stay **public**: the app reads the releases without signing in. A private repository answers 404,
and the page then shows "No published release was found on GitHub".

`build.ps1` still writes `dist\update.json` (version, SHA-256, notes). The GitHub flow does not need it, so attaching it
to the release is optional.

---

## 9. Code signing (strongly recommended)

An unsigned installer that asks for admin rights triggers SmartScreen ("Windows protected your PC")
and shows "Unknown publisher" in UAC. Sign it after building:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a dist\CardStudio-Setup.exe
```

For the best result, also sign `NPIC_CARD_APP\bin\Release\NPIC_CARD_APP.exe` **before** the setup is built.
Add a signing step to the app's `AfterBuild` target, or sign the app, then build the setup without `/t:Rebuild`.

---

## 10. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Build error `App build output not found` | The app failed to build. Fix the errors in `NPIC_CARD_APP` first |
| Build error `ZipDirectory task not found` | MSBuild is older than 15.8. Use VS 2019 16.x or newer |
| "This setup file is damaged: the application payload is missing" | The exe was built without the `BuildPayload` target. Rebuild with `build.ps1` |
| "already installed in X" during a silent install with `/D=` | Silent upgrades must use the existing folder. Omit `/D=` or uninstall first |
| Uninstall left `C:\Program Files\Card Studio` behind | The folder contains files the user added, or a file was in use. See the finish screen and `%TEMP%\CardStudio-Setup.log` |
| *Delete settings* removed nothing | `%AppData%` belongs to the account that approved UAC. If a different admin account elevated the uninstaller, delete `C:\Users\<user>\AppData\Roaming\NPIC_CARD_APP` by hand |
| Shortcut shows a blank icon | Explorer's icon cache. It refreshes after sign-out, or run `ie4uinit.exe -show` |

---

## 11. Serial numbers (activation)

Card Studio asks for a serial number when it opens, until one valid for **that PC** is entered.
It works offline, and every serial works on one PC only.

1. On the new PC, open Card Studio. The **Activate Card Studio** window shows the **Machine ID** (e.g. `EM3S-QXMF-403A-5466`); the user clicks **Copy** and sends it to you.
2. On your PC, make that PC's serial with the **Serial Generator** (`Tools\SerialGenerator`): paste the Machine ID
   (or the whole message it came in), type who it is for, press **Enter**. The serial is copied to the clipboard and
   logged to `serials.csv` beside the key; **Copy Message** gives a ready-to-send text with instructions in English and Khmer.
   Install it with its own setup (see *Installing the Serial Generator* below), or run it straight from the repo:
   ```powershell
   & "<MSBuild.exe>" Tools\SerialGenerator\SerialGenerator.csproj /p:Configuration=Release
   Tools\SerialGenerator\bin\Release\CardStudio.SerialGenerator.exe
   ```
   From the repo it finds `Installer\serial-private.key` by itself; elsewhere, use **Open Key…** (Ctrl+O) once: the location is remembered.
   The same can be done from the command line (same log file):
   ```powershell
   powershell -ExecutionPolicy Bypass -File Installer\serial.ps1 -MachineId EM3S-QXMF-403A-5466 -Name "Office PC 1"
   ```
3. Send the serial back; the user pastes it and clicks **Activate**. It is saved to `%ProgramData%\NPIC_CARD_APP\license.txt` (all Windows users of the PC) and checked again on every start.

### Installing the Serial Generator

For the PC (or PCs) of whoever makes serials, not for Card Studio users.

```powershell
powershell -ExecutionPolicy Bypass -File Installer\build-serial-generator.ps1                  # -> dist\admin\SerialGenerator-Setup.exe
powershell -ExecutionPolicy Bypass -File Installer\build-serial-generator.ps1 -Version 1.1.0   # also stamps GeneratorVersion.cs
```

`SerialGenerator-Setup.exe` works like Card Studio's setup (same engine): it installs to
`C:\Program Files\Card Studio Serial Generator`, adds a Start Menu shortcut (the desktop one is optional, off by default),
registers *Card Studio Serial Generator* in Settings > Apps with its own uninstaller, updates in place, and supports
`/S`, `/D=folder`, `/Desktop`, `/NoStartMenu`, and `uninstall.exe /uninstall [/S] [/purge]`. Log: `%TEMP%\SerialGenerator-Setup.log`.
It is separate from Card Studio: installing, updating or uninstalling one never touches the other.

- **The private key is not in the setup.** After installing, open the generator, click **Open Key…** and choose your copy
  of `serial-private.key` (e.g. from a USB stick or a protected folder). The generator remembers where it is; `serials.csv`
  is kept beside the key. If a key, `serials*.csv` or `key-path.txt` is ever left in `Tools\SerialGenerator\bin`, the
  build leaves it out of the setup and warns.
- **Keep it out of the Card Studio release.** It is written to `dist\admin\`, not `dist\`. Without the key it cannot make
  serials, but it is an admin tool and has no place on users' PCs.
- **Uninstall** keeps the remembered key location unless *Also forget the remembered key location* (`/purge`) is ticked.
  It never deletes the key file or `serials.csv`.
- **Version:** `Tools\SerialGenerator\GeneratorVersion.cs`, separate from Card Studio's `Version.cs`. Rebuild the generator
  (and its setup) whenever `serial-private.key` is replaced with `serial.ps1 -NewKey`: the generator checks that its key
  matches the public key it was built with.

### Expiry date

Every serial carries the **last day it works** (default: 1 year from today), or never expires. The date is part of the
signed text, so it cannot be edited; changing it makes the serial invalid.

- **Generator:** pick the date under **Expires** (or *6 months / 1 year / 2 years*, or **Never expires**).
- **Script:** 1 year by default, or `-Expires 2027-06-30`, `-Months 6`, `-NoExpiry`.
- **30 days before:** Card Studio says *License expires in N days* in the window title and status bar.
- **To renew:** make a new serial for the same Machine ID. The user enters it in **Settings & Help → License…**, which
  also shows the current expiry date. Renewing early is fine: the new serial replaces the old one.
- **After the last day:** the activation window opens again (*Your serial number expired on …*) until a new serial is entered.
- **Clock check:** Card Studio remembers the latest date it ran (`license-seen.txt` beside `license.txt`). If the PC's
  date is more than a day earlier, it asks for the correct date and time. Entering the same serial again doesn't help;
  a **new** serial from you resets the check (for a PC whose clock really was wrong). Serials that never expire skip this check.
- **Old serials:** serials made before expiry dates existed (105 characters, starting with `1`) still work and never expire.
  New serials have 108 characters and start with `2`.
- A serial that **never expires cannot be stopped later**: prefer a date, and renew it.

| | |
|---|---|
| Serial Generator | Admin only: **never give it to users** and never add it to Card Studio's setup (that one packs only `NPIC_CARD_APP\bin`); it has its own, `dist\admin\SerialGenerator-Setup.exe`. It shares `SerialFormat.cs` and `LicenseService.cs` with the app, so its serials always pass the app's check; it warns when its key does not match the app's public key |
| Private key | `Installer\serial-private.key`. **Git-ignored. Back it up**: without it no new serials can be made for the builds already out there |
| Public key | `PublicKey` in `NPIC_CARD_APP\Services\LicenseService.cs`; only the public key ships, so serials cannot be forged from the source or the exe |
| New key pair | `serial.ps1 -NewKey` (add `-Force` to replace one). Writes the new public key into the source; rebuild. **All serials made with the old key stop working** |
| Machine ID | From Windows' `MachineGuid`. It changes when Windows is reinstalled: that PC then needs a new serial |
| Status | System Info (Ctrl+Shift+I) → **License** shows the status, expiry date, Machine ID and license file; **Settings & Help → License…** shows the expiry date and takes a renewal |
| Log | `serials.csv` columns: Date, MachineId, Name, Expires (`yyyy-MM-dd` or `never`), Serial. Logs from before expiry dates get the Expires column added the next time a serial is made |
| Deactivate a PC | Delete `license.txt` (in `%ProgramData%\NPIC_CARD_APP` or `%AppData%\NPIC_CARD_APP`) |
