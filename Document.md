### v1.0.1.1

Release date: 2026-10-29

This version adds the Mobile App link (a phone connects to Card Studio over Wi-Fi), PDF export and a PDF Designer, Print Back, and a new way to pick students: a search box and the Student Gallery instead of the student table.

## Feature
* Mobile App… (sidebar, or Ctrl+Shift+M): connects the Card Studio phone app (the mobile folder) to this PC over the local Wi-Fi
* Mobile App page: Start / Stop, a pairing QR code, the PC address, Copy Link (paste it in the phone app if the camera cannot scan), and a list of what the phones did
* The phone scans the QR code once to pair. Every request needs the pairing code. New Pairing Code makes the old one stop working (use it if a phone is lost)
* The pairing code is stored encrypted for the Windows user, and is not included when settings are exported
* After 10 wrong codes from one address, that address is refused for a minute
* Allow in Firewall adds a Windows Firewall rule for private and domain networks only, never public Wi-Fi. Windows asks for administrator approval
* The link can start by itself when Card Studio starts. The port is 8765, and can be changed if another program uses it
* The phone can search and browse students, filter by no photo / not printed / no RFID card, and see every column of a student
* The phone can take or replace a student's photo, which is saved into the Photo Folder
* The phone can show a student's card (front or back), find a student by scanning a card QR code or ID, and see the latest printed cards
* The phone can ask Card Studio to print a card side, or the students selected on the PC. The person at the PC still confirms each tray load and inserts the tray
* A phone print is refused if the PC selection changed since the phone showed it, if another window is open on the PC, or if a card is printing
* The status bar shows "● Phone connected: name" while a phone is in touch (checked every 30 seconds). Click it to open the Mobile App page
* Preview & PDF… (sidebar, or Ctrl+Shift+V): shows the card design on paper at real size and exports it as a PDF, with no extra software
* Preview & PDF: students are the current one, the selected ones or every student in the list (a sample student when none is loaded)
* Preview & PDF: "A4 sheet" puts front and back side by side at real size with cut marks. "Card size" gives one card side per page, with the page the size of the card, for print shops
* Preview & PDF: ◀ ▶, Page Up / Page Down or the mouse wheel turn the pages, and Ctrl+S exports
* Preview & PDF → Student list: a table report of the students as a PDF. The standard NPIC list is A4 portrait with 0.5" margins, Times New Roman and Khmer OS Siemreap, the national motto and a title
* Student list → Page Design… (Ctrl+D): paper size (A4, Letter…) and orientation, margins, Latin and Khmer fonts and sizes, the text above and below the table, a heading on every page
* Page Design columns: choose what each shows, its header text, width and alignment, and the header shading. Widths are relative, so one design fits every paper size
* Page Design text can use [total], [total male], [total female], [name] and [date]. A built-in design cannot be changed; copy it into My designs first
* PDF Designer… (sidebar, or Ctrl+Shift+T): design your own PDF page. Write text anywhere (double-click the page), add tables and pictures, and export a PDF
* PDF Designer: drag to move (it snaps to the page centre and to other objects), arrow keys nudge (Shift = 5 mm), Ctrl+D or Alt+drag clones, plus align, distribute, undo / redo and zoom (Ctrl+wheel)
* PDF Designer: tables are edited in a cell grid (paste from Excel works), and column lines are dragged on the page. Long tables continue on the next page
* PDF Designer: A4, A5, Letter or Legal, portrait or landscape, with several pages. Pictures can come from a file or the clipboard
* PDF Designer data fields, filled from the student list: {StudentId} {NameKh} {NameEn} {Gender} {DateOfBirth} {Faculty} {Level} {Generation} {ValidUntil}, any column of the list such as {Phone}, and {No} {Count} {Date} {Page} {Pages}
* PDF Designer formats: {DateOfBirth:KhmerDate}, {StudentId:KhmerDigits}, {NameEn:Upper} and {Gender:KhmerGender}
* PDF Designer: "Repeat for each record" lists every student in a table. Exporting once per record makes one page per student, for certificates and letters
* PDF Designer: the data can come from the main list, the selected students, or any other Excel or CSV table. That table does not need a StudentId column
* PDF Designer: a preview with page navigation shows the design filled with real data. Designs are saved to %AppData%\NPIC_CARD_APP\PdfDesigns
* Print Back… (header, or Ctrl+Shift+P): prints any number of card backs (1 to 500) with no student chosen, tray load by tray load, using both slots when available
* Student Gallery: choose several students with Ctrl / Shift + click, or Ctrl+A for every tile shown. The button reads "Select 2 in List"
* Student Gallery opens with the students already selected in the main window
* Tray page: a mini map of the printer page shows where slot 1 and slot 2 land. Click a slot to edit its X / Y. A slot is amber when it is outside the page or slot 2 sits on slot 1
* Tray page: picture buttons for how the card lies in the tray (Horizontal, turn right 90°, turn left 90°), and a compact choice for cards per tray load
* Khmer translations for all of the above

## Change
* Main window: the student table is gone. Search a student by ID, name or faculty. Typing lists the matches, and a click (or ↓ ↑ and Enter) shows that student's front and back
* Main window: ◀ ▶ (Page Up / Page Down in the search box) step through the students
* Main window: at most 2 students can be selected to print: one card per tray slot, so one tray load. A third is refused until one is removed
* Main window: the selected students appear as chips under the search ("① NPIC25161 · name ✕"). Click a chip to show that student, ✕ removes it, Clear removes both
* Main window: "Select in List" in the Gallery narrows the main window to the chosen students, with a strip "Showing 2 students chosen in the Gallery" and a Show all button (or Esc in the search box)
* Main window: the Student card shows the details of the picked student, for example the date of birth

## Known issue
* Print Selected sends only 1 or 2 students at a time. For a large batch, pick the next two after each tray load
* Print Back does not add anything to the print history, because the cards belong to no student yet
* The Mobile App link uses plain HTTP (not encrypted). Use it only on a trusted private Wi-Fi, and show the QR code only to staff
* After an RFID card is saved, that student is gone from the list until the next start. To reprint a card first, reload the list with Import Students… (Ctrl+L). Print both sides before saving RFID numbers
* PCs that never chose a CSV folder now see the new, empty default CSV folder in CSV Files…, not the folder of their student file. Click Change Folder… to pick it again
* Cards printed in Classic before and after v1.0.0.8 look slightly different
* Export video needs Windows Media Foundation. Windows N / KN editions need the Media Feature Pack first
* Notifications are kept only until Card Studio is closed
* The user guide (F1) does not explain Card Template…, Card Designer…, Backup & Restore…, Preview & PDF…, PDF Designer… or Mobile App… yet, and notifications only appear in the shortcut list
* The user guide (F1 → Printing) still says to select students in the list with Ctrl+click or Shift+click, which no longer works
* The user guide (F1) does not mention that a student leaves the list after the RFID card is saved
* The user guide (F1 → Overview) still lists driver setup as a sidebar command
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.1.0

Release date: 2026-10-19

This version adds picture export to 3D View, and removes a student from the list once their RFID card is saved. Card Studio shows this version as 1.0.1 (in Settings > Apps and in Check for Updates).

## Feature
* 3D View: Export image (I) saves a picture of the card as it is shown, with the same angle, zoom and framing
* The picture is made larger than the screen, so the card keeps the detail of the card images
* Three file types: PNG with a transparent background, PNG with a dark background, or JPEG with a dark background. The type used last is remembered
* A transparent PNG is cropped to the card and its shadow, so it can be placed straight into a poster or document
* Export all images (Shift+I): saves the same picture for every student in the list, one file per student (e.g. NPIC24001-3D.png), into one folder
* The batch shows each card as it goes and a progress bar. Esc stops it, and the pictures already saved are kept
* If files with the same names are already in the folder, Card Studio asks before replacing them
* When the batch ends, it says how many pictures were saved, lists any that failed, and offers to open the folder
* After you save one picture, Card Studio offers to open it
* Video and image exports share one progress bar, and the toolbar drops the shortcut keys from the button names when the window is too narrow
* RFID: when a card is saved, the student is removed from the loaded list and the next student is selected, so the next card can be tapped straight away
* The status line says "removed from the list" after each saved card. This also applies when the card was already saved, or replaced
* Khmer translations for all of the above

## Change
* The student is removed only from the list on screen, not from the Excel, CSV, Google Sheet, API or SQL source. A list opened from a file comes back in full the next time Card Studio starts
* After cards are saved, the Total students tile and the Student Gallery show fewer students

## Known issue
* After an RFID card is saved, that student is gone from the list until the next start. To reprint a card first, reload the list with Import Students… (Ctrl+L). Print both sides before saving RFID numbers
* PCs that never chose a CSV folder now see the new, empty default CSV folder in CSV Files…, not the folder of their student file. Click Change Folder… to pick it again
* Cards printed in Classic before and after v1.0.0.8 look slightly different
* Export video needs Windows Media Foundation. Windows N / KN editions need the Media Feature Pack first
* Notifications are kept only until Card Studio is closed
* The user guide (F1) does not explain Card Template…, Card Designer… or Backup & Restore… yet, and notifications only appear in the shortcut list
* The user guide (F1) does not mention that a student leaves the list after the RFID card is saved
* The user guide (F1 → Overview) still lists driver setup as a sidebar command
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.0.9

Release date: 2026-10-19

This version adds Backup & Restore: all Card Studio data can be saved to one file and put back on the same or another PC.

## Feature
* Backup & Restore… (Settings & Help, or Ctrl+Shift+B): saves everything to one .zip file
* A backup holds settings, print history, RFID cards, logos, signatures, card designs, and optionally the student photos (with their _original copies) and the student CSV files
* The RFID file is included even when it is kept outside the app data folder, for example on a network share
* The license is never backed up: each PC keeps its own serial
* Back Up Now (Ctrl+S), Restore (Enter or double-click), Restore From File…, Copy To… (e.g. a USB drive), Show in Explorer, Delete (Del, to the Recycle Bin), Refresh (F5)
* Daily automatic backup when Card Studio starts (on by default). It runs in the background, has no photos, and keeps the last 10
* If the daily backup fails, the status bar says so and it tries again at the next start
* Before a restore, the current data is saved as a "before-restore" backup (the last 5 are kept). Nothing is restored if that safety backup fails
* A restore keeps files added since the backup, then restarts Card Studio
* Backups are kept in %AppData%\NPIC_CARD_APP\Backups
* Default folders: the Photo Folder is %AppData%\NPIC_CARD_APP\Photos and the CSV folder is %AppData%\NPIC_CARD_APP\CSV until you choose others. They are created when missing
* Folders… and CSV Files…: new "Use Default" button to go back to the default folder (files are not moved)
* Folders… lists the Backups folder, and marks the Photo and CSV folders when they are the defaults

## Known issue
* PCs that never chose a CSV folder now see the new, empty default CSV folder in CSV Files…, not the folder of their student file. Click Change Folder… to pick it again
* Cards printed in Classic before and after v1.0.0.8 look slightly different
* Export video needs Windows Media Foundation. Windows N / KN editions need the Media Feature Pack first
* Notifications are kept only until Card Studio is closed
* The user guide (F1) does not explain Card Template…, Card Designer… or Backup & Restore… yet, and notifications only appear in the shortcut list
* The user guide (F1 → Overview) still lists driver setup as a sidebar command
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.0.8

Release date: 2026-10-19

This version redesigns Settings & Help and All Settings, moves the language switch to the top bar, and adjusts the Classic card layout.

## Feature
* Language menu in the top bar: a flag button (🇬🇧 EN ▾ / 🇰🇭 KH ▾) opens a menu with English and Khmer, the language in use ticked. Ctrl+Shift+L still works
* Settings & Help is now a grouped list: Appearance · Settings & files · Printer · Help & system. Use ↑ ↓ to move between rows
* Each Settings & Help row shows its current state on the right: dark mode switch, theme colour swatch, language flag, installed version, and license status (Active / Expires soon / Expired / Not activated)
* New Language row in Settings & Help
* All Settings: a left column with All Settings, Needs attention, and one item per section. Sections with problems show how many in orange
* All Settings: "Needs attention" shows only the settings that have a problem ("Nothing needs attention." when all is fine)
* All Settings: one card per section. Rows that need attention are tinted orange, and rows with a file or folder get an open button
* All Settings: right-click a row to Copy value, Copy setting and value, or Open in Explorer. Ctrl+C copies "Setting: value"
* All Settings: ↑ ↓ Home End move between rows, and Enter or double-click opens a file or folder
* Card Designer: new "Space left (mm)" and "Space right (mm)" for text and fields, to keep empty space around the value
* Card Designer: new "Map" picture (a map pin) in the Image menu

## Change
* Classic card, front: the label column is wider, so the colons line up and the values start 0.6 mm after them
* Classic card, front: "ជំនាន់ទី" (generation) has its own aligned colon
* Classic card, front: the director stamp, signature and name moved a little to the left
* Classic card, back: a map pin is added after the Facebook, Telegram, YouTube and TikTok icons, which are now 3 mm apart
* A Classic design copied into My designs matches these changes, and the social icons and map pin are grouped so they move together
* The sidebar header (logo and app name) stays at the top while the commands below it scroll
* The Language item was removed from the sidebar

## Known issue
* Cards printed in Classic before and after this update look slightly different. Finish a batch before updating
* Export video needs Windows Media Foundation. Windows N / KN editions need the Media Feature Pack first
* Notifications are kept only until Card Studio is closed
* The user guide (F1) does not explain Card Template… or Card Designer… yet, and notifications only appear in the shortcut list
* The user guide (F1 → Overview) still lists driver setup as a sidebar command
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

### v1.0.0.7

Release date: 2026-10-19

This version adds a 3D card preview with video export, a notification bell, a collapsible sidebar, and much stronger Card Designer tools.

## Feature
* 3D View (sidebar, or Shift+F11): the card is shown as a real card (CR80, 0.76 mm thick, rounded corners) that you can turn, flip and zoom
* 3D View controls: drag to turn, double-click or Space to flip, F / B to face front / back, A auto-spin, R reset, mouse wheel or + − to zoom
* In 3D View, ← → (or PageUp / PageDown) move to the previous / next student, Home / End to the first / last. Esc closes
* Export video (V): records the card turning as a 1920 × 1080, 30 fps, 8-second .mp4 file. Esc cancels, and an unfinished file is deleted
* Notification bell: warnings, errors and finished jobs are kept in a list under the bell (Ctrl+Shift+N)
* The bell shows an unread badge, red when there is an unread warning or error
* Click a notification to open the page it is about. The × removes one, and Clear all empties the list
* Sidebar: show icons only or icons with names (Ctrl+\). The choice is remembered. In icon-only mode a tooltip shows each name
* Card Template…: right-click a built-in design (or click "Copy as new design") to copy it into My designs, then edit it in Card Designer or use it as it is
* Card Designer: new shapes, Triangle and Curve, which point Up / Down / Left / Right
* Card Designer: rotate shapes with the rotate knob (Shift = 15° steps, snaps to 0 / 90 / 180 / 270°) or Rotate 90° left / right. Resizing follows the turned shape
* Card Designer: select several elements (Ctrl / Shift + click, drag a box, Ctrl+A) and edit their shared properties at once
* Card Designer: Group (Ctrl+G) and Ungroup (Ctrl+Shift+G). Double-click a grouped element to edit it alone
* Card Designer: Align left / centre / right and top / middle / bottom, distribute with equal gaps, and same width / height as the key element
* Card Designer: "Line up with the card" aligns to the card instead of to each other
* Card Designer: layers move together, and copies are added just above the top selected layer
* System Info: Windows edition and build (Windows 11 is detected correctly), processor, memory and free disk space
* System Info: the card design in use and the number of saved designs, and the web API, Google Sheet and SQL Server sources. Passwords, tokens and keys are never shown
* Khmer translations for all of the above

## Change
* Popups now dim every monitor, not just the one they are on. The dim follows when a monitor is plugged in or removed
* The language switch moved from the sidebar to the top bar: a flag button (EN ▾ / KH ▾) opens a menu with English and Khmer, the language in use ticked
* The sidebar header (logo and app name) stays at the top while the commands below it scroll
* Copy System Info includes the new PC, card design and source details

## Known issue
* Export video needs Windows Media Foundation. Windows N / KN editions need the Media Feature Pack first
* Notifications are kept only until Card Studio is closed
* The user guide (F1) does not explain Card Template… or Card Designer… yet, and notifications only appear in the shortcut list
* The user guide (F1 → Overview) still lists driver setup as a sidebar command
* Help still lists .xls files, but import only accepts .xlsx / .xlsm

---

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
