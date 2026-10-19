### v1.0.0.6

Release date: 2026-10-18

This version shows Card Studio in Khmer: every window can be switched between English and Khmer.

## Feature
* Language switch: EN / KH at the bottom of the sidebar, Settings & Help… → Language, or Ctrl+Shift+L
* Switching asks first, saves the choice and restarts the app. Settings are kept, and a student list opened from a file comes back
* Khmer covers every window: main window, import, photos, printing, RFID, history, gallery, settings, activation, Card Template and Card Designer
* Buttons, labels, tabs, list columns, menus, tool tips, search hints and messages are translated
* Card Text and Card Designer property lists show Khmer names and descriptions
* Long Khmer text wraps between words instead of inside a word (uses the Khmer dictionary built into Windows 10 1903 and later)
* Text without a translation stays in English, so nothing breaks
* The user guide (F1) explains the language switch and lists Ctrl+Shift+L

## Change
* The language only changes the app windows. Printed cards are not affected: their text still comes from Card Text and the card design
* The user guide keeps its own English | ខ្មែរ switch

## Known issue
* The user guide (F1) does not explain Card Template… or Card Designer… yet
* The user guide (F1 → Overview) still lists driver setup as a sidebar command
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.0.5

Release date: 2026-10-18

This version adds new card designs and a Card Designer to make your own cards.

## Feature
* Card Template… (sidebar): choose the card design used for preview and printing
* 4 built-in designs: Classic (the original NPIC card), Modern (navy header band), Minimal (clean white card, large name) and Ribbon (navy ribbon down the left edge)
* Every design is previewed live with the selected student and the current Card Text, so what you see is what prints
* The photo box is the same size in every design, so photos cropped with Remove Background… fit all of them
* Card Designer… (sidebar): build your own front and back
* Add text, student fields, the photo, QR codes, images and shapes (rectangle, ellipse, line)
* Move and resize elements on a live preview, and edit every detail in the properties panel
* Fonts: Khmer OS Siemreap, Khmer OS Muol, Khmer OS Muol Light, Siemreap, Tacteing (ornament)
* Field formats: as it is, CAPITAL LETTERS, Khmer digits (2025 → ២០២៥) or Khmer date (24/01/2008 → ២៤ មករា ២០០៨)
* Fields can have a label in front, e.g. "អត្តលេខ: NPIC24001"
* Images: Upload image…, drag and drop, or paste a picture with Ctrl+V. Change picture… keeps the element's box
* Layers: bring forward / send backward, bring to front / send to back, lock, snap
* Undo / redo (Ctrl+Z / Ctrl+Y), copy / paste (Ctrl+C / Ctrl+V), duplicate (Ctrl+D), delete (Del)
* Arrow keys move the selected element 0.1 mm (Shift + arrow = 1 mm)
* My designs: create, rename, duplicate, delete, import and export designs
* Save (Ctrl+S) keeps the design. Save & Use also prints with it
* Unsaved changes are never lost: Close or Esc asks first
* Designs are saved to %AppData%\NPIC_CARD_APP\CardDesigns
* Custom fields: every column of the student list is kept (e.g. Phone, BloodType), for all import sources, and can be printed with a Card Designer field
* Slim scroll bars now also on property lists and formatted text (release notes, user guide)

## Change
* Pictures chosen in Card Text and Card Designer are copied to the app data folder, so cards still print if the original file is moved

## Known issue
* The user guide (F1) does not explain Card Template… or Card Designer… yet
* The user guide (F1 → Overview) still lists driver setup as a sidebar command
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.0.4

Release date: 2026-10-18

This version makes the sidebar shorter and adds background removal to the Student Gallery.

## Feature
* Student Gallery: new "Remove Background… (Ctrl+B)" button for the selected student
* After removing the background, the gallery refreshes that student's photo and thumbnail straight away
* The button also works when only the kept original photo is left, so it can be restored from the gallery
* Settings & Help: new "Epson Driver Setup…" tile (Ctrl+Shift+E)

## Change
* Sidebar: "Google Sheets" removed. Use Import Students… (Ctrl+L) → Google Sheets, or Ctrl+Shift+G
* Sidebar: "Epson Driver Setup…" removed. Use Settings & Help… → Epson Driver Setup…, the "Driver Setup…" button in Print setup → Printer, or Ctrl+Shift+E
* Settings & Help page is taller, with tiles in 5 rows instead of 4

## Known issue
* The user guide (F1 → Overview) still lists driver setup as a sidebar command
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.0.3

Release date: 2026-10-18

This version adds license activation (each PC needs a serial number) and installs the card fonts automatically.

## Feature
* License activation: Card Studio asks for a serial number before it opens
* Each PC shows its Machine ID (XXXX-XXXX-XXXX-XXXX). Send it to the administrator, who sends back a serial for that PC
* A serial works only on the PC it was made for, and cannot be edited or forged
* Serials have an expiry date (default 1 year) or never expire
* 30 days before expiry, the window title and status bar say "License expires in N days"
* Settings & Help → License… shows the expiry date and takes a new serial to renew it
* After the expiry date, the activation window opens again until a new serial is entered
* Clock check: if the PC's date is turned back, Card Studio asks for the correct date and time
* System Info shows the license status, expiry date, Machine ID and license file
* The license is saved for all Windows users of the PC (%ProgramData%\NPIC_CARD_APP\license.txt)
* Card fonts are included: Khmer OS Muol, Khmer OS Muol Light, Khmer OS Siemreap, Siemreap, Tacteing
* The setup installs missing card fonts for all users
* If a font is still missing, Card Studio installs it for the current user at start-up (no administrator needed) and restarts once

## Admin tool
* New Serial Generator (Tools\SerialGenerator): paste a Machine ID, pick the expiry date, copy the serial
* Expiry options: a date, 6 months, 1 year, 2 years or never expires
* Every serial made is logged to serials.csv (date, Machine ID, name, expiry, serial)
* Also available as a script: Installer\serial.ps1
* The Serial Generator is for the administrator only and is not part of the setup

## Known issue
* After updating to v1.0.0.3, every PC must be activated before Card Studio opens. Collect the Machine IDs and send the serials before updating
* Reinstalling Windows changes the Machine ID: that PC then needs a new serial
* Keep a backup of Installer\serial-private.key (not in Git). Without it, no new serials can be made
* PCs with a 1.0.2 build will not be offered 1.0.0.3, because 1.0.0.3 is a lower number
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.0.2

Release date: 2026-10-18

This version makes Check for Updates work with no setup: Card Studio now gets new versions from its GitHub releases.

## Feature
* Check for Updates (Ctrl+U) reads the latest release from GitHub: https://github.com/sannsovichika99/NPIC_CARD_APP/releases
* No update address to enter: the "Update address" box is removed from the Check for Updates page
* The download is verified with the SHA-256 that GitHub publishes for CardStudio-Setup.exe
* Release notes are shown formatted (headings, bold, lists, tables), including Khmer text
* "Check Now" renamed to "Check Again (F5)"
* When the check fails, the page shows what to do: check the internet connection, or download the setup from the releases page
* System Info shows "Updates from" (the releases page) and any new version that is available
* The user guide (F1) explains that updates come from GitHub and need the internet

## Fix
* Four-part versions are shown correctly: update.json, Settings > Apps and the GitHub tag now all show 1.0.0.2 (was 1.0.0)
* build.ps1 accepts 1.2.0 or 1.2.0.1 with -Version
* Removed the unused update address setting (UpdateUrl)

## Known issue
* PCs on v1.0.0.1 with no update address set will not find this version: install v1.0.0.2 by hand once, then later updates arrive by themselves
* PCs with a 1.0.2 build will not be offered 1.0.0.2, because 1.0.0.2 is a lower number
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.0.1

Release date: 2026-10-17

Card Studio prints student ID cards for NPIC (National Polytechnic Institute of Cambodia) on an Epson card
printer, and saves the RFID chip number of every card it prints.

## Feature
* Import students from Excel (.xlsx / .xlsm), CSV, Google Sheets, web API or SQL Server
* Save an empty Excel template with the correct columns
* CSV Files manager: check, preview, create, rename, delete or load student CSV files
* Search students by ID, name or faculty
* Find each student's photo by student ID (NPIC24001.jpg)
* Download photos from Google Drive or OneDrive
* Crop photos and remove the background (original photo kept and can be restored)
* Card preview, front and back, at real size (85.6 × 54 mm) in Khmer and English
* QR code, director signature, stamp and logos on the card
* Card Text editor for fixed text, issue date and signature
* Full-screen preview and millimetre grid for checking positions
* 3D card preview: turn, flip and zoom the card (Shift+F11)
* Print front and back, 1 or 2 cards per tray load
* Tray calibration: slot X / Y, test print, card orientation, rotate 180°
* Check Printer and Print Queue before printing
* Epson Driver Setup: paper source, paper type and quality
* Save RFID card numbers from a USB reader (duplicate cards refused)
* RFID Cards list: saved cards and students without a card
* Print History: who, which side, which printer, when
* Student Gallery with photo and print status
* All Settings page with Export / Import
* Folders manager and System Info
* Dark / Light mode and Theme Color
* User guide in English and Khmer (F1)
* Single-file installer with shortcuts and uninstaller
* Check for Updates (Ctrl+U) with SHA-256 check

## Known issue
* Version lowered from 1.0.2.0 to 1.0.0.1: PCs with 1.0.2 will not be offered this update
* update.json and Settings > Apps show 1.0.0 instead of 1.0.0.1
* Help lists .xls files, but import only accepts .xlsx / .xlsm
