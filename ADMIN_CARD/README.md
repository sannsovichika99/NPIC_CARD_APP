# Card Studio Admin (ADMIN_CARD)

The administrator's tool for every PC on the network that runs Card Studio (NPIC_CARD_APP).
It finds the PCs by itself and shows their status. It can also send commands to the PCs you select.

> **Admin only.** Never give ADMIN_CARD or `admin-private.key` to users. Whoever has the key can send commands to every PC.

## What it shows

| Column | Meaning |
|---|---|
| Status | **Online** = Card Studio answered within the last 45 s. PCs that are off stay listed as **Offline**. |
| Computer / Label | Windows computer name, plus your own label (Label…), e.g. "Room 204". |
| Address | IPv4 address used for commands. |
| Version | Card Studio version. Older than the newest version in the list = **(old)**, in orange. |
| License | Expiry date and days left. Orange = 30 days or less, red = 7 days or less, expired, or not valid. |
| Phones | Phones paired through Mobile App that are connected now (`off` = Mobile App is stopped). |
| Students / Today | Students loaded, and card sides printed today. |
| Busy | Why the PC cannot take a command now (a window is open, a card is printing). |

The panel on the right shows everything about the selected PC: Machine ID, user, Windows version, printer, the phones' names, print totals, the PC's clock and the result of the last command.
**Export CSV…** saves the list for Excel.

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
