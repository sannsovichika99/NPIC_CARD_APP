# Card Studio Mobile

Phone companion for **Card Studio** (the NPIC ID card printer app in `../NPIC_CARD_APP`).
The phone connects to Card Studio on the PC over the local Wi-Fi. Nothing goes through the internet.

| Tab | What it does |
| --- | --- |
| **Home** | The paired PC and its student list, card progress (photos taken, cards printed, RFID cards saved), a *To do* list that opens the student list already filtered, shortcuts to the other tabs and the latest prints. |
| **Students** | The student list loaded in Card Studio: search (ID, Khmer or Latin name, faculty), filter *No photo / Not printed / No RFID card*. |
| **Student** | Photo, details, the card front/back drawn by Card Studio with the design in use, print and RFID status. **Take photo** crops to the card's photo shape and saves it on the PC as `{StudentId}.jpg`. The card on the PC updates straight away. |
| **Check Card** | Scan the QR code on a printed card (or type an ID) to see whose card it is, whether it was printed, its RFID number and whether it is still valid. |
| **Printed** | The latest printed card sides from Card Studio's print history. |
| **PC** | The connected PC, list source and counts. **Forget this PC**. |

## Connect a phone

1. On the PC, open Card Studio, then **Mobile App…** in the sidebar (or **Ctrl+Shift+M**), and press **Start**.
   The first time, Windows asks whether Card Studio may use the network: allow **Private networks**.
   You can also press **Allow in Firewall** (needs admin rights).
2. Put the phone on the **same Wi-Fi** as the PC.
3. In the phone app, tap **Scan PC QR code** and scan the code on the PC screen.

If the camera can't scan, press **Copy Link** on the PC, send the link to the phone, and use **Paste link**.
You can also type the address, port and pairing code by hand.
Scanning the QR code with the phone's own camera app also opens the app and pairs it (`npiccard://` link).

**Start when Card Studio starts** is ticked automatically after the first Start, so phones reconnect without opening the page again.

## Security

- Every request except `/api/ping` needs the pairing code (about 130 bits, random). It is stored with Windows DPAPI on the PC and in the secure keychain/keystore on the phone.
- After 10 wrong codes from one address, that address is refused for one minute.
- **New Pairing Code** on the PC disconnects every phone at once (use it if a phone is lost).
- The firewall rule from **Allow in Firewall** covers private and domain networks only, never public Wi-Fi.
- Traffic is plain HTTP inside the local network. Use it on the school's staff Wi-Fi, not on a guest or public network.
- A replaced photo is never deleted. It is moved to `_replaced\{StudentId}-{time}.jpg` in the photo folder, together with any *Remove Background* original that belonged to it.

## Run it (development)

Requires Node 20+. The phone needs the **Expo Go** app ([Android](https://play.google.com/store/apps/details?id=host.exp.exponent) / [iOS](https://apps.apple.com/app/expo-go/id982107779)).

```bash
cd mobile
npm install
npx expo start          # scan the terminal QR code with Expo Go (same Wi-Fi as this PC)
npx expo start --web    # or try it in a browser (camera scanning needs a phone)
```

Checks:

```bash
npx tsc --noEmit
npx expo-doctor
```

## Build an installable app

Uses [EAS Build](https://docs.expo.dev/build/introduction/) in the cloud, so no Android Studio or Xcode is needed:

```bash
npx eas-cli@latest login
npx eas-cli@latest build:configure
npx eas-cli@latest build --platform android --profile preview   # .apk to install directly on staff phones
```

`app.json` already allows plain HTTP to the PC: `usesCleartextTraffic` on Android, and `NSAllowsLocalNetworking` plus the local network prompt text on iOS.

## How it connects (for developers)

The PC side is `NPIC_CARD_APP/Services/MobileLinkServer.cs`: a small HTTP/JSON server on `TcpListener`, so it needs no admin rights or URL reservation.
`MainForm.Mobile.cs` hands it the loaded students and runs card rendering on the UI thread. `MobileLinkForm.cs` is the PC page.
The phone side is `src/lib/api.ts`.

The pairing QR code contains `npiccard://pair?h=<ip1,ip2>&p=<port>&t=<code>&n=<pc-name>`. The phone tries each address in order.

| Method | Path | |
| --- | --- | --- |
| GET | `/api/ping` | App, version, PC name (no code needed) |
| GET | `/api/info` | Counts, list source, card design, theme colour |
| GET | `/api/students?q=&filter=nophoto\|notprinted\|nocard&offset=&limit=` | Loaded students |
| GET | `/api/students/{id}` | One student, with every imported column |
| GET / PUT | `/api/students/{id}/photo` | Read / replace the photo (JPEG or PNG body, max 15 MB) |
| GET | `/api/students/{id}/card?side=front\|back&w=` | The card as JPEG, drawn with the current design |
| POST | `/api/students/{id}/print?side=front\|back` | Selects the student and side in Card Studio and starts **Print Selected** (the tray load is confirmed on the PC). `409` when a print is already running or another window is open on the PC |
| GET | `/api/lookup?code=` | Student for a scanned card QR / ID (also searches the print history) |
| GET | `/api/history?limit=` | Latest printed card sides |

Send the code as `Authorization: Bearer <code>`. Image URLs may use `?token=<code>` instead.
The optional `X-Device-Name` header is the name shown in the PC's activity list.

The app reads only the list currently loaded in Card Studio. It never changes the Excel, CSV, Google Sheet, API or SQL source.
