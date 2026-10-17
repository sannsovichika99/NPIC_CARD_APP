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
