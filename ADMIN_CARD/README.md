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
