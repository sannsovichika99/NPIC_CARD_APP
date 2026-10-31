# Card Studio Admin (ADMIN_CARD)

The administrator's tool for every PC on the network that runs Card Studio (NPIC_CARD_APP).
It finds the PCs by itself and shows their status. It can also send commands to the PCs you select.

> **Admin only.** Never give ADMIN_CARD or `admin-private.key` to users. Whoever has the key can send commands to every PC.

## Look and feel

ADMIN_CARD is built from **Card Studio's own UI kit**: it compiles `UiKit.cs`, `ModalKit.cs`, `AccentPalette.cs`, `SlimScroll.cs` and `Notifications.cs` straight from `NPIC_CARD_APP`. A design change in Card Studio therefore shows up in ADMIN_CARD at the next build.
`Kit\CardStudioKit.cs` provides small, English-only versions of the few app classes the kit needs (`Lang`, `CardFonts`, `AppSettings`, `Branding`).

- **Layout:** same as Card Studio's main window.
  - Collapsible sidebar with the logo. Its sections are *PCs* (All / Online / Needs Attention, with counts), *Actions*, *Network* and *App*.
  - Greeting header with the notification bell.
  - Four gradient tiles: PCs online, phones connected, printed today, needs attention.
  - Two shadow cards: **Computers** (search and grid) and **Details** (facts and command buttons for the selection).
- **Popups:** every popup is a Card Studio `ModalForm` (accent strip, icon badge, pill buttons, dimmed backdrop). Messages use `AppDialog`.
- **Theme:** follows Card Studio on this PC. Dark mode and the primary colour are read from `%AppData%\NPIC_CARD_APP\settings.xml`. The *Dark Mode / Light Mode* item in the sidebar overrides the theme for ADMIN_CARD only, saved in `%AppData%\CardStudio.Admin\settings.xml` (ADMIN_CARD restarts to apply it).
- **Bell:** it lists new PCs found, PCs that went offline, licenses that need attention, a missing or wrong admin key, and the result of every command. Clicking a notice selects that PC.
- **Shortcuts:**

  | Keys | Action |
  |---|---|
  | F5 | Refresh |
  | Ctrl+F | Search |
  | Ctrl+M | Send Message |
  | Ctrl+U | Check for Updates |
  | Ctrl+E | Export CSV |
  | Ctrl+Shift+N | Notifications |
  | Ctrl+Shift+V | Versions page |
  | Ctrl+C | Copy Machine ID |
  | Delete | Remove from the list |

  Double-click a PC to send it a message. Right-click a PC for the menu.

## What it shows

| Column | Meaning |
|---|---|
| Status | Chip: **Online** = Card Studio answered within the last 45 s, **Busy** = a window is open or a card is printing there (the reason is in the tooltip), **Offline** = PC off or Card Studio closed. Offline PCs stay listed. |
| Computer | Windows computer name, with your own label (Label…, e.g. "Room 204") underneath. |
| Address | IPv4 address used for commands. |
| Version | Card Studio version. Older than the newest version in the list = **old**, in orange. |
| License | "Until 2027-10-01", or "12 days left" in orange from 30 days. Red = 7 days or less, expired, or not valid. The full text is in the tooltip. |
| Phones | Phones paired through Mobile App that are connected now (`off` = Mobile App is stopped). |
| Today | Card sides printed today. |

The **Details** card shows everything about the selected PC: last seen, Machine ID, user, Windows version, students loaded, printer, the phones' names, print totals, the PC's clock and the result of the last command.
**Export CSV…** saves the list for Excel.

## Versions page

To open it, choose **Versions…** in the sidebar, or press Ctrl+Shift+V. The sidebar badge counts the PCs on an older version.

- **Every release:** the page lists every Card Studio release from `Document.md`, the changelog at the root of the repo, newest first.
  - It also lists any other version a PC runs. A version newer than the notes is marked *Newer than notes*; that happens when ADMIN_CARD was built before the release.
  - Each row shows its release date and first sentence, and how many PCs run it (a green dot means one of them is online).
  - Chips mark the *Latest* release, and the older versions still in use (*Update*).
- **Summary strip:** the latest release, how many PCs are up to date, and **Update All Older PCs**. That button opens Check for Updates on every online PC that runs an older version.
- **Selected version:**
  - Its release date, and how many releases it is behind the latest.
  - The PCs that run it; click one to show it in the main list.
  - **Update These PCs**.
  - Its full release notes: summary, then Feature / Change / Fix / Known issue.
- **Notes come from the build:** `Document.md` is embedded in ADMIN_CARD.exe when it is built (`ReleaseNotes.cs` reads it). After a release, update `Document.md` and rebuild ADMIN_CARD.
- **"Old" marker:** the latest release is also what the main list's "old" marker compares against. A network where every PC runs 1.0.1 is still shown as out of date once 1.0.1.1 is in the notes.

## Network Devices page: find PCs and install Card Studio

To open it, choose **Network Devices…** in the sidebar, or press Ctrl+Shift+D.

### What it shows

It scans the networks of this PC when it opens. **Scan Again** scans again, and **Stop** stops a scan.

- **Networks scanned:**
  - Every physical adapter of this PC. Networks larger than /22 are limited to the /24 around this PC.
  - The extra addresses from **Addresses…**.
  - A /24 takes about 15 seconds.
- **How devices are found:**
  - ARP, which answers even when a device's firewall blocks ping.
  - Ping.
  - The Windows file-sharing port, TCP 445.
  - The name from DNS, else NetBIOS.
- **Type column:**
  - *Card Studio*: the address is a PC in the main list.
  - *Windows PC*: file sharing answers, so Card Studio can be installed from here.
  - *Device*: anything else, such as a phone, printer, router, or a PC that blocks file sharing.
  - *This PC*: the PC ADMIN_CARD runs on.
- **Filters:** All, Windows PCs, Without Card Studio.
- **Double-click:**
  - On a Card Studio PC, it selects that PC in the main list.
  - On a Windows PC, it opens the install dialog.
- **Copy List** copies the table for Excel.

### Install Card Studio…

Select one or more Windows PCs, then click **Install Card Studio…**. The install works the way IT deployment tools do (`RemoteInstaller.cs`).

1. **Setup file.** The page finds `dist\CardStudio-Setup.exe`, or you choose the file with **Browse…**. The page remembers it and shows its version.
2. **Administrator.** An administrator account of the target PCs, such as `DOMAIN\admin` or `PC-NAME\Administrator`. Leave it empty to use your own Windows account, which works for a domain administrator.
   - The user name is remembered.
   - The password is only kept while the window is open, and is never saved.
3. **Check Access.** For each PC, it connects with WMI and shows its Windows version and its **Machine ID**. The Machine ID is read from the PC's MachineGuid, with the same formula as Card Studio (`LicenseService.MachineIdFrom`).
   - **Copy Machine IDs** copies them for the Serial Generator.
   - Paste each serial in the **Serial** column. Each serial is checked against its PC before anything is installed.
4. **Install.** On each PC, four PCs at a time:
   1. Copies the setup to `ADMIN$\Temp`.
   2. Runs it silently through WMI (`CardStudio-Setup.exe /S`, plus `/NoDesktop` if the desktop shortcut box is cleared).
   3. Reads its exit code back and translates it into a message: Card Studio open, .NET 4.8.1 missing, and so on. On failure it also shows the end of the setup log.
   4. Deletes the copied files.
   5. Saves the serial to `C:\ProgramData\NPIC_CARD_APP\license.txt`, so Card Studio opens straight away. That is also what makes the PC show up in the main list.

**What the target PCs need:**
- **File and Printer Sharing** and **Windows Management Instrumentation (WMI)** allowed in Windows Firewall, on a Private or domain network. A domain usually allows both by Group Policy.
- An **administrator account** of the PC.
- **.NET Framework 4.8.1**, built into Windows 11.

**Account names:** type `PC-NAME\user`, `DOMAIN\user`, or a Microsoft account e-mail address with its password (not the PIN). A bare `user` is sent as `TARGET-PC\user` for each PC; otherwise Windows would check it against this PC's accounts.

**Networks without a domain (home, small office, most school labs):** "Access denied" means Windows blocks remote administration for local and Microsoft accounts until the PC is prepared, and its firewall blocks file sharing and WMI on Public networks.

1. Click **Save Preparation Script…** (on the install dialog or the Network Devices page). It saves `Prepare-CardStudio-Install.cmd`.
2. Run it once on each PC: right-click it → **Run as administrator**. It makes three changes:
   - It sets `LocalAccountTokenFilterPolicy = 1`.
   - It enables Windows' File and Printer Sharing rules for **Private and Domain** networks only.
   - It adds its own WMI rules (`CardStudioAdmin-DCOM`, `CardStudioAdmin-WMI`), also for Private and Domain only. Windows' built-in WMI rule for private networks would also open public ones.
3. It then offers to set the PC's networks to Private. Answer **Y** only on a trusted network.

To undo it:

```
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy /f
```

and, in PowerShell:

```
Remove-NetFirewallRule -Name 'CardStudioAdmin-*'
```

Card Studio cannot be updated this way while it is open on the PC. Use **Check for Updates** for PCs that already run it.

## Commands (on the selected PCs)

- **Message…** shows the text on the PC in a box and keeps it under Card Studio's bell. If a window is open on that PC, the message goes only to the bell.
- **Check for Updates** opens Card Studio's update page on the PC. If the PC is busy, Card Studio asks the user to press Ctrl+U instead.
- **Install Serial…** sends a new serial number (license) to one PC. Make the serial with the Serial Generator for that PC's Machine ID (**Copy Machine ID**). ADMIN_CARD checks that the serial is for that PC before it sends it.
- **Disconnect Phones** makes a new mobile pairing code. Every phone paired with that PC must scan the QR code again.

## How it works

```
ADMIN_CARD ──UDP 8766 broadcast "discover" (signed)──▶ every Card Studio on this network
           ◀──────────── status reply (JSON) ──────────
ADMIN_CARD ──UDP 8766 command to one PC (signed)──────▶ Card Studio runs it once and replies
```

- **Shared code:** both sides use `NPIC_CARD_APP\Services\AdminProtocol.cs`. Card Studio runs `AdminAgent.cs`, which is connected to the main window in `MainForm.Admin.cs`.
- **Signing:** each command is signed with `Installer\admin-private.key` (ECDSA P-256). Card Studio checks the signature against `AdminProtocol.PublicKey`, which is compiled into the app. Packets that are unsigned or forged get **no reply**, so other people on the network cannot even see that Card Studio is running.
- **Replay protection:** a command is refused if its time is more than 10 minutes away from the PC's clock. ADMIN_CARD then shows a clock error. Each nonce runs only once, and a resent copy gets the first reply again.
- **No secrets in the status:** it contains no pairing code, no serial and no student data, only counts.
- **Saved data:** the PC list, labels and extra addresses are saved in `%AppData%\CardStudio.Admin\devices.xml`.

## Requirements

- Card Studio must be a build that includes the admin agent, and it must be **open** on the PC.
- **Windows Firewall:** the Card Studio setup adds the inbound rule *Card Studio Admin* (UDP 8766, private and domain networks only), and uninstall removes it. If a PC got Card Studio without the setup, add the rule by hand:
  `netsh advfirewall firewall add rule name="Card Studio Admin" dir=in action=allow protocol=UDP localport=8766 profile=private,domain program="C:\Program Files\Card Studio\NPIC_CARD_APP.exe"`
- **PCs behind a router:** broadcasts stay on their own network. Add these PCs with **Addresses…**: an address (`192.168.2.15`), a range (`192.168.2.10-60`), a network (`192.168.2.0/24`) or a computer name.
- **Admin PC on a *Public* network:** Windows may block the replies. Set that network to Private.

## The key

On start, ADMIN_CARD looks for the key in this order: the key chosen last time, `admin-private.key` next to the exe, then `Installer\admin-private.key` in any folder above the exe.
`Installer\admin-private.key` is git-ignored. **Keep a backup** somewhere safe: if you lose it, you need a new key and a new Card Studio build.

To replace the key (it was lost or leaked):

1. Choose **Key ▸ Create New Key…**.
2. Copy the public key it shows into `AdminProtocol.PublicKey`.
3. Rebuild Card Studio and install it on every PC.

## Build

Open `NPIC_CARD_APP.sln` and build **ADMIN_CARD**. The output is `ADMIN_CARD\bin\<Configuration>\ADMIN_CARD.exe` (.NET Framework 4.8.1).
ADMIN_CARD is not part of `CardStudio-Setup.exe`.
