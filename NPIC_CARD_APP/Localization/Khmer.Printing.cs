namespace NPIC_CARD_APP
{
    // Epson Driver Setup, Print Queue and the printer checks (Printing\*.cs).
    internal static partial class KhmerStrings
    {
        private static void Printing()
        {
            // ---- Page description (Print setup card, Check Printer) ----
            A("Paper: {0}", "ក្រដាស៖ {0}");
            A("Paper: {0}  |  Source: {1}  |  Hard margin: {2} / {3} mm", "ក្រដាស៖ {0}  |  ប្រភព៖ {1}  |  រឹមរឹង៖ {2} / {3} mm");
            A("Source: {0}", "ប្រភព៖ {0}");
            A("Hard margin: {0} / {1} mm", "រឹមរឹង៖ {0} / {1} mm");

            // ---- Problems found before printing (CardPrinter, PrinterStatus) ----
            A("Printer not found: {0}", "រកមិនឃើញម៉ាស៊ីនបោះពុម្ព៖ {0}");
            A("Saved driver settings could not be applied. Open 'Driver Setup...' again.",
                "មិនអាចអនុវត្តការកំណត់ driver ដែលបានរក្សាទុកបានទេ។ សូមបើក 'ការកំណត់ Driver…' ម្ដងទៀត។");
            A("Saved driver settings could not be applied. Open 'Open Epson Settings…' again.",
                "មិនអាចអនុវត្តការកំណត់ driver ដែលបានរក្សាទុកបានទេ។ សូមបើក 'បើកការកំណត់ Epson…' ម្ដងទៀត។");
            A("Saved driver settings are damaged. Open 'Driver Setup...' again.",
                "ការកំណត់ driver ដែលបានរក្សាទុកខូច។ សូមបើក 'ការកំណត់ Driver…' ម្ដងទៀត។");
            A("Saved driver settings are damaged. Open 'Open Epson Settings…' again.",
                "ការកំណត់ driver ដែលបានរក្សាទុកខូច។ សូមបើក 'បើកការកំណត់ Epson…' ម្ដងទៀត។");
            A("Saved driver settings are for \"{0}\", not \"{1}\".", "ការកំណត់ driver ដែលបានរក្សាទុកគឺសម្រាប់ \"{0}\" មិនមែន \"{1}\" ទេ។");
            A("Open 'Driver Setup...' again.", "សូមបើក 'ការកំណត់ Driver…' ម្ដងទៀត។");
            A("Saved driver settings are for \"{0}\", not \"{1}\". Open 'Open Epson Settings…' again.",
                "ការកំណត់ driver ដែលបានរក្សាទុកគឺសម្រាប់ \"{0}\" មិនមែន \"{1}\" ទេ។ សូមបើក 'បើកការកំណត់ Epson…' ម្ដងទៀត។");
            A("Windows says the printer settings are not valid for \"{0}\". Check that the printer is installed, then open 'Driver Setup...' again.",
                "Windows ថាការកំណត់ម៉ាស៊ីនបោះពុម្ពមិនត្រឹមត្រូវសម្រាប់ \"{0}\"។ ពិនិត្យថាម៉ាស៊ីនបោះពុម្ពត្រូវបានដំឡើង រួចបើក 'ការកំណត់ Driver…' ម្ដងទៀត។");
            A("Windows could not send the job to \"{0}\" (error {1}: {2}).", "Windows មិនអាចផ្ញើការងារទៅ \"{0}\" បានទេ (កំហុស {1}៖ {2})។");
            A("Check that the printer is on and connected, then click 'Check Printer'.",
                "ពិនិត្យថាម៉ាស៊ីនបោះពុម្ពបើក និងបានភ្ជាប់ រួចចុច 'ពិនិត្យម៉ាស៊ីនបោះពុម្ព'។");
            A("'Driver Setup...' has not been done, so Paper Source / Paper Type are still the driver defaults.",
                "មិនទាន់បានធ្វើ 'ការកំណត់ Driver…' ទេ ដូច្នេះប្រភពក្រដាស / ប្រភេទក្រដាស នៅតែជាលំនាំដើមរបស់ driver។");
            A("Paper Source is \"{0}\", not the ID card tray. Fix it in 'Driver Setup...'.",
                "ប្រភពក្រដាសគឺ \"{0}\" មិនមែនថាសកាត ID ទេ។ កែវានៅ 'ការកំណត់ Driver…'។");
            A("Slot 2 (X {0}, Y {1} mm) overlaps slot 1 (X {2}, Y {3} mm). Change the Slot 2 X / Y and check it with 'Test Print'.",
                "រន្ធ 2 (X {0}, Y {1} mm) ត្រួតលើរន្ធ 1 (X {2}, Y {3} mm)។ ប្ដូរ X / Y របស់រន្ធ 2 ហើយពិនិត្យវាដោយ 'បោះពុម្ពសាកល្បង'។");
            A("Slot {0} (X {1}, Y {2} mm, card {3} x {4} mm) is outside the page ({5} x {6} mm), so it will print blank. Change the Slot X / Y.",
                "រន្ធ {0} (X {1}, Y {2} mm, កាត {3} x {4} mm) នៅក្រៅទំព័រ ({5} x {6} mm) ដូច្នេះវានឹងបោះពុម្ពទទេ។ ប្ដូរ X / Y របស់រន្ធ។");
            A("(Could not read printer status from Windows: {0})", "(មិនអាចអានស្ថានភាពម៉ាស៊ីនបោះពុម្ពពី Windows បានទេ៖ {0})");
            A("Windows shows the printer as OFFLINE (USB/Wi-Fi not connected, printer off, or 'Use Printer Offline' is ticked in the print queue).",
                "Windows បង្ហាញម៉ាស៊ីនបោះពុម្ពថា OFFLINE (USB/Wi-Fi មិនបានភ្ជាប់ ម៉ាស៊ីនបោះពុម្ពបិទ ឬ 'Use Printer Offline' ត្រូវបានធីកក្នុងជួរបោះពុម្ព)។");
            A("The printer has stopped printing (PrinterStatus = Stopped).", "ម៉ាស៊ីនបោះពុម្ពបានឈប់បោះពុម្ព (PrinterStatus = Stopped)។");
            A("The printer is offline (PrinterStatus = Offline).", "ម៉ាស៊ីនបោះពុម្ពនៅក្រៅបណ្ដាញ (PrinterStatus = Offline)។");
            A("Printer reports: {0}.", "ម៉ាស៊ីនបោះពុម្ពរាយការណ៍៖ {0}។");
            A("{0} job(s) already waiting in the print queue (first: \"{1}\"). The printer prints them first — click 'Print Queue...' and cancel the old jobs.",
                "មានការងារ {0} កំពុងរង់ចាំក្នុងជួរបោះពុម្ពរួចហើយ (ទីមួយ៖ \"{1}\")។ ម៉ាស៊ីនបោះពុម្ពនឹងបោះពុម្ពពួកវាមុន — ចុច 'ជួរបោះពុម្ព…' ហើយបោះបង់ការងារចាស់ៗ។");
            A("{0} job(s) already waiting in the print queue (first: \"{1}\", status: {2}). The printer prints them first — click 'Print Queue...' and cancel the old jobs.",
                "មានការងារ {0} កំពុងរង់ចាំក្នុងជួរបោះពុម្ពរួចហើយ (ទីមួយ៖ \"{1}\", ស្ថានភាព៖ {2})។ ម៉ាស៊ីនបោះពុម្ពនឹងបោះពុម្ពពួកវាមុន — ចុច 'ជួរបោះពុម្ព…' ហើយបោះបង់ការងារចាស់ៗ។");
            A("low paper", "ក្រដាសជិតអស់");
            A("no paper / no card tray", "គ្មានក្រដាស / គ្មានថាសកាត");
            A("low ink", "ទឹកថ្នាំជិតអស់");
            A("no ink", "អស់ទឹកថ្នាំ");
            A("cover open", "គម្របបើក");
            A("paper jam", "ក្រដាសជាប់");
            A("offline", "ក្រៅបណ្ដាញ");
            A("service requested", "ត្រូវការការថែទាំ");
            A("output tray full", "ថាសចេញពេញ");

            // ---- Epson Driver Setup ----
            A("Epson Driver Setup", "ការកំណត់ Driver Epson");
            A("Epson printers only — paper source, paper type and quality used for the cards",
                "សម្រាប់ម៉ាស៊ីនបោះពុម្ព Epson ប៉ុណ្ណោះ — ប្រភពក្រដាស ប្រភេទក្រដាស និងគុណភាពដែលប្រើសម្រាប់កាត");
            A("Refresh (F5)", "ផ្ទុកឡើងវិញ (F5)");
            A("Windows Printer Properties", "លក្ខណៈម៉ាស៊ីនបោះពុម្ព Windows");
            A("Reset to Driver Defaults", "កំណត់ទៅលំនាំដើម Driver វិញ");
            A("Driver", "Driver");
            A("Port", "ច្រក");
            A("EPSON PRINTERS ON THIS PC", "ម៉ាស៊ីនបោះពុម្ព EPSON លើកុំព្យូទ័រនេះ");
            A("DRIVER SETTINGS CARD STUDIO WILL PRINT WITH", "ការកំណត់ DRIVER ដែល CARD STUDIO នឹងប្រើបោះពុម្ព");
            A("For PVC cards on the L8050, choose in the Epson dialog: Paper Source = ID Card Tray · Paper Type = PVC ID Card · Quality = High · " +
              "Borderless off · Copies = 1. Click OK to save.",
                "សម្រាប់កាត PVC លើ L8050 ជ្រើសក្នុងប្រអប់ Epson៖ Paper Source = ID Card Tray · Paper Type = PVC ID Card · Quality = High · " +
                "Borderless បិទ · Copies = 1។ ចុច OK ដើម្បីរក្សាទុក។");
            A("Open Epson Settings…", "បើកការកំណត់ Epson…");
            A("In use", "កំពុងប្រើ");
            A("Card printer", "ម៉ាស៊ីនបោះពុម្ពកាត");
            A("Epson printer", "ម៉ាស៊ីនបោះពុម្ព Epson");
            A("None installed. Install the Epson L8050 driver from epson.com, then click Refresh.",
                "មិនទាន់ដំឡើងទេ។ ដំឡើង driver Epson L8050 ពី epson.com រួចចុច ផ្ទុកឡើងវិញ។");
            A("Other printers", "ម៉ាស៊ីនបោះពុម្ពផ្សេងទៀត");
            A("Not listed here — this page is for Epson printers only.", "មិនបង្ហាញនៅទីនេះទេ — ទំព័រនេះសម្រាប់ម៉ាស៊ីនបោះពុម្ព Epson ប៉ុណ្ណោះ។");
            A("Could not read the driver settings: {0}", "មិនអាចអានការកំណត់ driver បានទេ៖ {0}");
            A("Yes — Card Studio prints the cards on this printer", "បាទ/ចាស — Card Studio បោះពុម្ពកាតលើម៉ាស៊ីនបោះពុម្ពនេះ");
            A("No — saving its Epson settings makes it the card printer", "ទេ — ការរក្សាទុកការកំណត់ Epson របស់វា ធ្វើឱ្យវាជាម៉ាស៊ីនបោះពុម្ពកាត");
            A("Windows cannot open this printer. Reinstall the Epson driver, then click Refresh.",
                "Windows មិនអាចបើកម៉ាស៊ីនបោះពុម្ពនេះបានទេ។ ដំឡើង driver Epson ឡើងវិញ រួចចុច ផ្ទុកឡើងវិញ។");
            A("Saved settings", "ការកំណត់ដែលបានរក្សាទុក");
            A("Saved ({0} bytes of Epson driver data)", "បានរក្សាទុក (ទិន្នន័យ driver Epson {0} បៃ)");
            A("None — the Epson driver defaults below are used. Click 'Open Epson Settings…' to set them.",
                "គ្មាន — លំនាំដើម driver Epson ខាងក្រោមត្រូវបានប្រើ។ ចុច 'បើកការកំណត់ Epson…' ដើម្បីកំណត់វា។");
            A("Paper source", "ប្រភពក្រដាស");
            A("{0}  —  should be the ID card tray", "{0}  —  គួរតែជាថាសកាត ID");
            A("Paper size", "ទំហំក្រដាស");
            A("Paper type", "ប្រភេទក្រដាស");
            A("Choose 'PVC ID Card' in the Epson dialog (Windows cannot read this value back)",
                "ជ្រើស 'PVC ID Card' ក្នុងប្រអប់ Epson (Windows មិនអាចអានតម្លៃនេះត្រឡប់មកវិញបានទេ)");
            A("Orientation", "ទិស");
            A("Landscape  —  Card Studio always prints portrait, so this is ignored",
                "ផ្ដេក  —  Card Studio តែងតែបោះពុម្ពបញ្ឈរ ដូច្នេះមិនអើពើនឹងនេះ");
            A("Portrait", "បញ្ឈរ");
            A("Print quality", "គុណភាពបោះពុម្ព");
            A("Greyscale  —  the cards will print without colour", "ប្រផេះ  —  កាតនឹងបោះពុម្ពគ្មានពណ៌");
            A("Copies", "ចំនួនច្បាប់");
            A("{0}  —  every tray load will print {1} times", "{0}  —  ថាសនីមួយៗនឹងបោះពុម្ព {1} ដង");
            A("Printer status", "ស្ថានភាពម៉ាស៊ីនបោះពុម្ព");
            A("Ready", "រួចរាល់");
            A("Driver default", "លំនាំដើម Driver");
            A("High", "ខ្ពស់");
            A("Medium", "មធ្យម");
            A("Low", "ទាប");
            A("Draft", "ព្រាង");
            A("Windows cannot open \"{0}\".", "Windows មិនអាចបើក \"{0}\" បានទេ។");
            A("Reinstall the Epson driver, then click Refresh.", "ដំឡើង driver Epson ឡើងវិញ រួចចុច ផ្ទុកឡើងវិញ។");
            A("The Epson driver dialog could not be opened:", "មិនអាចបើកប្រអប់ driver Epson បានទេ៖");
            A("\"{0}\" is now the card printer. Epson driver settings saved — check the list above for anything marked ! or ✕.",
                "\"{0}\" ឥឡូវជាម៉ាស៊ីនបោះពុម្ពកាត។ បានរក្សាទុកការកំណត់ driver Epson — ពិនិត្យបញ្ជីខាងលើរកអ្វីដែលមានសញ្ញា ! ឬ ✕។");
            A("Epson driver settings saved — check the list above for anything marked ! or ✕.",
                "បានរក្សាទុកការកំណត់ driver Epson — ពិនិត្យបញ្ជីខាងលើរកអ្វីដែលមានសញ្ញា ! ឬ ✕។");
            A("Forget the saved Epson settings for \"{0}\"?", "បំភ្លេចការកំណត់ Epson ដែលបានរក្សាទុកសម្រាប់ \"{0}\"?");
            A("Paper source and paper type go back to the driver defaults, which usually print on A4 paper instead of the ID card tray.",
                "ប្រភពក្រដាស និងប្រភេទក្រដាសនឹងត្រឡប់ទៅលំនាំដើម driver ដែលជាធម្មតាបោះពុម្ពលើក្រដាស A4 ជំនួសឱ្យថាសកាត ID។");
            A("Saved settings removed — the Epson driver defaults are used.", "បានដកការកំណត់ដែលបានរក្សាទុក — លំនាំដើម driver Epson ត្រូវបានប្រើ។");
            A("Could not open the Windows printer properties:", "មិនអាចបើកលក្ខណៈម៉ាស៊ីនបោះពុម្ព Windows បានទេ៖");
            A("Could not save the settings: {0}", "មិនអាចរក្សាទុកការកំណត់បានទេ៖ {0}");
            A("OFFLINE", "ក្រៅបណ្ដាញ");
            A("Offline", "ក្រៅបណ្ដាញ");
            A("Needs attention", "ត្រូវការការយកចិត្តទុកដាក់");

            // ---- Print Queue ----
            A("Print Queue", "ជួរបោះពុម្ព");
            A("Cancel Selected", "បោះបង់ដែលបានជ្រើស");
            A("Cancel All", "បោះបង់ទាំងអស់");
            A("Open Windows Queue", "បើកជួរ Windows");
            A("Job", "ការងារ");
            A("Document", "ឯកសារ");
            A("Pages", "ទំព័រ");
            A("Submitted", "បានដាក់ស្នើ");
            A("Owner", "ម្ចាស់");
            A("Reading queue…", "កំពុងអានជួរ…");
            A("Could not read the print queue from Windows: {0}", "មិនអាចអានជួរបោះពុម្ពពី Windows បានទេ៖ {0}");
            A("Blocked", "ត្រូវបានរារាំង");
            A("Printing", "កំពុងបោះពុម្ព");
            A("Waiting", "កំពុងរង់ចាំ");
            A("Paused", "បានផ្អាក");
            A("Spooling", "កំពុងរៀបចំ");
            A("Deleting", "កំពុងលុប");
            A("Queue is empty — the next card job will print immediately.", "ជួរទទេ — ការងារកាតបន្ទាប់នឹងបោះពុម្ពភ្លាមៗ។");
            A("{0} job(s) in the queue. The printer prints them in order; cancel old or stuck jobs before printing new cards.",
                "មានការងារ {0} ក្នុងជួរ។ ម៉ាស៊ីនបោះពុម្ពបោះពុម្ពតាមលំដាប់ សូមបោះបង់ការងារចាស់ ឬជាប់គាំង មុនបោះពុម្ពកាតថ្មី។");
            A("Printer ready.  {0}", "ម៉ាស៊ីនបោះពុម្ពរួចរាល់។  {0}");
            A("Auto-refresh every 2 s.  Updated {0}.", "ផ្ទុកឡើងវិញស្វ័យប្រវត្តិរៀងរាល់ 2 វិនាទី។  បានធ្វើបច្ចុប្បន្នភាព {0}។");
            A("Cancel job {0}?", "បោះបង់ការងារ {0}?");
            A("Cancel {0} jobs?", "បោះបង់ការងារ {0}?");
            A("If a card is printing right now, it will stop part-way.", "ប្រសិនបើកាតកំពុងបោះពុម្ពឥឡូវនេះ វានឹងឈប់ពាក់កណ្ដាលទី។");
            A("Job {0}: {1}", "ការងារ {0}៖ {1}");
            A("Some jobs could not be cancelled:", "ការងារខ្លះមិនអាចបោះបង់បានទេ៖");
            A("Jobs from another user need administrator rights — use 'Open Windows Queue' instead.",
                "ការងារពីអ្នកប្រើផ្សេងត្រូវការសិទ្ធិអ្នកគ្រប់គ្រង — សូមប្រើ 'បើកជួរ Windows' ជំនួសវិញ។");
            A("Could not open the Windows print queue:", "មិនអាចបើកជួរបោះពុម្ព Windows បានទេ៖");
        }
    }
}
