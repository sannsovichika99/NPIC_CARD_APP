namespace NPIC_CARD_APP
{
    // Shared words, the main window and the Settings & Help page.
    internal static partial class KhmerStrings
    {
        /// <summary>Words used on many pages: buttons, sides, yes / no.</summary>
        private static void Common()
        {
            A("OK", "យល់ព្រម");
            A("Cancel", "បោះបង់");
            A("Close", "បិទ");
            A("Yes", "បាទ/ចាស");
            A("No", "ទេ");
            A("Abort", "បញ្ឈប់");
            A("Retry", "ព្យាយាមម្ដងទៀត");
            A("Ignore", "មិនអើពើ");
            A("Restore", "ស្ដារ");
            A("Maximize", "ពង្រីកពេញ");
            A("Save", "រក្សាទុក");
            A("Save…", "រក្សាទុក…");
            A("Delete", "លុប");
            A("Delete…", "លុប…");
            A("Rename…", "ប្ដូរឈ្មោះ…");
            A("Open", "បើក");
            A("Open…", "បើក…");
            A("Open Folder", "បើកថត");
            A("Open File", "បើកឯកសារ");
            A("Browse…", "រកមើល…");
            A("Change…", "ផ្លាស់ប្ដូរ…");
            A("Choose…", "ជ្រើសរើស…");
            A("Refresh", "ផ្ទុកឡើងវិញ");
            A("Copy", "ចម្លង");
            A("Export…", "នាំចេញ…");
            A("Import…", "នាំចូល…");
            A("Apply", "អនុវត្ត");
            A("Reset", "កំណត់ឡើងវិញ");
            A("Default", "លំនាំដើម");
            A("Remove", "ដកចេញ");
            A("Back", "ក្រោយ");
            A("Front", "មុខ");
            A("front", "មុខ");
            A("back", "ក្រោយ");
            A("FRONT", "ខាងមុខ");
            A("BACK", "ខាងក្រោយ");
            A("Both", "ទាំងពីរ");
            A("Auto", "ស្វ័យប្រវត្តិ");
            A("None", "គ្មាន");
            A("Ready.", "រួចរាល់។");
            A("Error", "កំហុស");
            A("Error: {0}", "កំហុស៖ {0}");
            A("Warning", "ការព្រមាន");
            A("Name", "ឈ្មោះ");
            A("Status", "ស្ថានភាព");
            A("File", "ឯកសារ");
            A("Folder", "ថត");
            A("Students", "និស្សិត");
            A("Student", "និស្សិត");
            A("Printer", "ម៉ាស៊ីនបោះពុម្ព");
            A("Tray", "ថាស");
            A("Side", "ផ្នែក");
            A("Date", "កាលបរិច្ឆេទ");
            A("Time", "ម៉ោង");
            A("Size", "ទំហំ");
            A("Modified", "កែប្រែចុងក្រោយ");
            A("Location", "ទីតាំង");
            A("Value", "តម្លៃ");
            A("Setting", "ការកំណត់");
            A("Settings", "ការកំណត់");
            A("Photo", "រូបថត");
            A("Photos", "រូបថត");
            A("never", "មិនដែល");
            A("today (last day)", "ថ្ងៃនេះ (ថ្ងៃចុងក្រោយ)");
            A("tomorrow", "ថ្ងៃស្អែក");
            A("in {0} days", "ក្នុងរយៈពេល {0} ថ្ងៃទៀត");
            A("(Press Ctrl+C to copy this message.)", "(ចុច Ctrl+C ដើម្បីចម្លងសារនេះ។)");
            A("If the file is open in Excel, close it and try again.",
                "ប្រសិនបើឯកសារកំពុងបើកក្នុង Excel សូមបិទវា រួចព្យាយាមម្ដងទៀត។");
            A("Could not save the file:", "មិនអាចរក្សាទុកឯកសារបានទេ៖");
            A("Exported {0} row(s) to:", "បាននាំចេញ {0} ជួរទៅ៖");
            A("Printed cards are not affected.", "កាតដែលបានបោះពុម្ពមិនមានការប៉ះពាល់ទេ។");
            A("The app restarts to apply it. Your settings are saved and a student list opened from a file comes back by itself; " +
              "students imported from the API or SQL Server must be loaded again.",
                "កម្មវិធីនឹងបើកឡើងវិញដើម្បីអនុវត្ត។ ការកំណត់របស់អ្នកត្រូវបានរក្សាទុក ហើយបញ្ជីនិស្សិតដែលបានបើកពីឯកសារ " +
                "នឹងត្រឡប់មកវិញដោយខ្លួនឯង។ និស្សិតដែលបាននាំចូលពី API ឬ SQL Server ត្រូវផ្ទុកម្ដងទៀត។");
        }

        /// <summary>Sidebar, header, stat tiles, student list, card preview and the Print setup card.</summary>
        private static void MainWindow()
        {
            // ---- Sidebar ----
            A("ID Card Printer", "បោះពុម្ពកាតសម្គាល់ខ្លួន");
            A("Dashboard", "ផ្ទាំងគ្រប់គ្រង");
            A("Find a student, check the card preview and print (Ctrl+F searches)",
                "ស្វែងរកនិស្សិត មើលកាតជាមុន និងបោះពុម្ព (Ctrl+F ដើម្បីស្វែងរក)");
            A("Gallery", "វិចិត្រសាល");
            A("Photos and details of every student; shows missing photos and print status (Ctrl+G)",
                "រូបថត និងព័ត៌មានលម្អិតរបស់និស្សិតគ្រប់រូប បង្ហាញរូបថតដែលខ្វះ និងស្ថានភាពបោះពុម្ព (Ctrl+G)");
            A("Print History", "ប្រវត្តិបោះពុម្ព");
            A("Cards already printed: who, which side, when (Ctrl+H)",
                "កាតដែលបានបោះពុម្ពរួច៖ អ្នកណា ផ្នែកណា ពេលណា (Ctrl+H)");
            A("Import Students…", "នាំចូលនិស្សិត…");
            A("Load the student list from Excel, CSV, Google Sheets, the web API or SQL Server, or save an Excel template (Ctrl+L)",
                "ផ្ទុកបញ្ជីនិស្សិតពី Excel, CSV, Google Sheets, web API ឬ SQL Server ឬរក្សាទុកគំរូ Excel (Ctrl+L)");
            A("CSV Files…", "ឯកសារ CSV…");
            A("Student CSV files in one folder: check, preview, create, rename, delete or load them (Ctrl+Shift+C)",
                "ឯកសារ CSV និស្សិតក្នុងថតតែមួយ៖ ពិនិត្យ មើលជាមុន បង្កើត ប្ដូរឈ្មោះ លុប ឬផ្ទុកវា (Ctrl+Shift+C)");
            A("RFID Cards", "កាត RFID");
            A("Card numbers saved for printed cards, and loaded students who have no card yet (Ctrl+Shift+R)",
                "លេខកាតដែលបានរក្សាទុកសម្រាប់កាតដែលបានបោះពុម្ព និងនិស្សិតដែលបានផ្ទុកតែមិនទាន់មានកាត (Ctrl+Shift+R)");
            A("Print Queue", "ជួរបោះពុម្ព");
            A("See or cancel jobs waiting in Windows", "មើល ឬបោះបង់ការងារដែលកំពុងរង់ចាំក្នុង Windows");
            A("CARD", "កាត");
            A("Photo Folder…", "ថតរូបថត…");
            A("Choose or open the folder with photos named {StudentId}.jpg / .png, or download photos into it from Google Drive " +
              "(Ctrl+Shift+D) or OneDrive (Ctrl+Shift+O)",
                "ជ្រើស ឬបើកថតដែលមានរូបថតដាក់ឈ្មោះ {StudentId}.jpg / .png ឬទាញយករូបថតចូលវាពី Google Drive " +
                "(Ctrl+Shift+D) ឬ OneDrive (Ctrl+Shift+O)");
            A("Remove Background…", "លុបផ្ទៃខាងក្រោយ…");
            A("Crop the selected student's photo and remove the wall / screen behind it (Ctrl+B)",
                "កាត់រូបថតនិស្សិតដែលបានជ្រើស ហើយលុបជញ្ជាំង / ផ្ទាំងនៅពីក្រោយ (Ctrl+B)");
            A("Card Template…", "គំរូកាត…");
            A("Choose the card design (Classic, Modern, Minimal, Ribbon or your Custom design) with a live preview of front and back",
                "ជ្រើសការរចនាកាត (បុរាណ ទំនើប សាមញ្ញ ខ្សែបូ ឬការរចនាផ្ទាល់ខ្លួន) ដោយមើលផ្នែកមុខ និងក្រោយផ្ទាល់");
            A("Card Designer…", "រចនាកាត…");
            A("Design your own card: place text, student fields, photo, QR codes, images and shapes on the front and back",
                "រចនាកាតផ្ទាល់ខ្លួន៖ ដាក់អត្ថបទ ព័ត៌មាននិស្សិត រូបថត កូដ QR រូបភាព និងរាងនានាលើផ្នែកមុខ និងក្រោយ");
            A("Card Text…", "អត្ថបទលើកាត…");
            A("Edit the fixed text printed on the cards and choose the director signature",
                "កែអត្ថបទថេរដែលបោះពុម្ពលើកាត និងជ្រើសហត្ថលេខានាយក");
            A("APP", "កម្មវិធី");
            A("Full Screen", "អេក្រង់ពេញ");

            // ---- Notifications (bell in the header) ----
            A("Notifications", "ការជូនដំណឹង");
            A("Notifications: warnings, errors and finished jobs (Ctrl+Shift+N)",
                "ការជូនដំណឹង៖ ការព្រមាន កំហុស និងការងារដែលបានបញ្ចប់ (Ctrl+Shift+N)");
            A("Clear all", "សម្អាតទាំងអស់");
            A("{0} new", "ថ្មី {0}");
            A("Just now", "មុននេះបន្តិច");
            A("{0} min ago", "{0} នាទីមុន");
            A("{0} h ago", "{0} ម៉ោងមុន");
            A("You're all caught up", "គ្មានការជូនដំណឹងថ្មីទេ");
            A("Warnings, errors and finished jobs appear here.", "ការព្រមាន កំហុស និងការងារដែលបានបញ្ចប់ នឹងបង្ហាញនៅទីនេះ។");
            A("Preview error", "កំហុសការមើលជាមុន");
            A("Something went wrong", "មានបញ្ហាកើតឡើង");
            A("Card files missing", "ខ្វះឯកសារកាត");
            A("Print history not saved", "មិនបានរក្សាទុកប្រវត្តិបោះពុម្ព");
            A("No students found", "រកមិនឃើញនិស្សិត");
            A("Something is missing", "មានអ្វីមួយខ្វះ");
            A("No card printer found", "រកមិនឃើញម៉ាស៊ីនបោះពុម្ពកាត");
            A("Tray slots", "រន្ធថាស");
            A("Printing stopped", "បានឈប់បោះពុម្ព");
            A("License expiring soon", "អាជ្ញាប័ណ្ណជិតផុតកំណត់");
            A("Update available", "មានកំណែថ្មី");
            A("Printing finished", "បោះពុម្ពរួចរាល់");
            A("Students loaded", "បានផ្ទុកនិស្សិត");
            A("Photos downloaded", "បានទាញយករូបថត");
            A("RFID card saved", "បានរក្សាទុកកាត RFID");
            A("Settings imported", "បាននាំចូលការកំណត់");
            A("Card design saved", "បានរក្សាទុកការរចនាកាត");

            A("Collapse sidebar", "បង្រួមរបារចំហៀង");
            A("Expand sidebar", "ពង្រីករបារចំហៀង");
            A("Show icons only (Ctrl+\\)", "បង្ហាញតែរូបតំណាង (Ctrl+\\)");
            A("Show icons and names (Ctrl+\\)", "បង្ហាញរូបតំណាង និងឈ្មោះ (Ctrl+\\)");
            A("Language: switch between English and Khmer (Ctrl+Shift+L)", "ភាសា៖ ប្ដូររវាងអង់គ្លេស និងខ្មែរ (Ctrl+Shift+L)");
            A("Full-screen card preview (F11)", "មើលកាតជាមុនពេញអេក្រង់ (F11)");
            A("Settings & Help…", "ការកំណត់ និងជំនួយ…");
            A("{0}, Theme Color, Language (Ctrl+Shift+L), Folders (Ctrl+Shift+F), All Settings (Ctrl+,), " +
              "Epson Driver Setup (Ctrl+Shift+E), System Info (Ctrl+Shift+I), Check for Updates (Ctrl+U) and Help & About (F1) in one page",
                "{0} ពណ៌ចម្បង ភាសា (Ctrl+Shift+L) ថត (Ctrl+Shift+F) ការកំណត់ទាំងអស់ (Ctrl+,) " +
                "ការកំណត់ Driver Epson (Ctrl+Shift+E) ព័ត៌មានប្រព័ន្ធ (Ctrl+Shift+I) ពិនិត្យរកកំណែថ្មី (Ctrl+U) និងជំនួយ និងអំពី (F1) ក្នុងទំព័រតែមួយ");
            A("LANGUAGE", "ភាសា");

            // ---- Header and stat tiles ----
            A("Good morning", "អរុណសួស្តី");
            A("Good afternoon", "ទិវាសួស្តី");
            A("Good evening", "សាយណ្ហសួស្តី");
            A("Good morning, {0}", "អរុណសួស្តី {0}");
            A("Good afternoon, {0}", "ទិវាសួស្តី {0}");
            A("Good evening, {0}", "សាយណ្ហសួស្តី {0}");
            A("Find a student, check the card, then print.", "ស្វែងរកនិស្សិត ពិនិត្យកាត រួចបោះពុម្ព។");
            A("Side of the card that Print Selected sends to the printer",
                "ផ្នែកនៃកាតដែល បោះពុម្ពដែលបានជ្រើស ផ្ញើទៅម៉ាស៊ីនបោះពុម្ព");
            A("Print side", "ផ្នែកបោះពុម្ព");
            A("Print Selected", "បោះពុម្ពដែលបានជ្រើស");
            A("Print the selected students, tray load by tray load (Ctrl+P)",
                "បោះពុម្ពនិស្សិតដែលបានជ្រើស ម្ដងមួយថាសៗ (Ctrl+P)");
            A("Print Back…", "បោះពុម្ពខាងក្រោយ…");
            A("Print copies of the card back — the same for every student, no student needed (Ctrl+Shift+P)",
                "បោះពុម្ពច្បាប់ចម្លងនៃខាងក្រោយកាត — ដូចគ្នាសម្រាប់និស្សិតទាំងអស់ មិនចាំបាច់ជ្រើសនិស្សិត (Ctrl+Shift+P)");

            // ---- Print Back popup (PrintBackForm) ----
            A("Print Back", "បោះពុម្ពខាងក្រោយ");
            A("The back is the same for every student — no student needs to be selected",
                "ខាងក្រោយកាតដូចគ្នាសម្រាប់និស្សិតទាំងអស់ — មិនចាំបាច់ជ្រើសនិស្សិតទេ");
            A("Uses \"Rotate back 180°\" and the slot positions from Print setup. Blank backs are not added to the print history.",
                "ប្រើ \"បង្វិលផ្នែកក្រោយ 180°\" និងទីតាំងរន្ធពី ការរៀបចំបោះពុម្ព។ ខាងក្រោយទទេមិនត្រូវបានបញ្ចូលក្នុងប្រវត្តិបោះពុម្ពទេ។");
            A("1 tray load", "1 ថាស");
            A("{0} tray loads", "{0} ថាស");
            A("Print", "បោះពុម្ព");
            A("Slot 1: Back side", "រន្ធ 1៖ ផ្នែកខាងក្រោយ");
            A("Slot 2: Back side", "រន្ធ 2៖ ផ្នែកខាងក្រោយ");
            A("Printing cancelled after {0} card back(s).", "បានបោះបង់ការបោះពុម្ព បន្ទាប់ពីខាងក្រោយកាត {0}។");
            A("Done: {0} card back(s) printed.", "រួចរាល់៖ បានបោះពុម្ពខាងក្រោយកាត {0}។");
            A("Total students", "និស្សិតសរុប");
            A("No list loaded", "មិនទាន់ផ្ទុកបញ្ជី");
            A("Selected to print", "បានជ្រើសដើម្បីបោះពុម្ព");
            A("Search for a student", "ស្វែងរកនិស្សិតម្នាក់");
            A("Printed today", "បោះពុម្ពថ្ងៃនេះ");
            A("Open the student gallery (Ctrl+G)", "បើកវិចិត្រសាលនិស្សិត (Ctrl+G)");
            A("Open the print history (Ctrl+H)", "បើកប្រវត្តិបោះពុម្ព (Ctrl+H)");
            A("Nothing printed yet", "មិនទាន់បានបោះពុម្ពទេ");
            A("{0} card sides printed in total", "បានបោះពុម្ពសរុប {0} ផ្នែកកាត");
            A("Print history unavailable", "មិនអាចប្រើប្រវត្តិបោះពុម្ពបាន");
            A("{0} match the search", "{0} ត្រូវនឹងការស្វែងរក");
            A("{0} chosen in the Gallery", "{0} បានជ្រើសក្នុងវិចិត្រសាល");
            A("Showing {0} student chosen in the Gallery", "បង្ហាញនិស្សិត {0} នាក់ ដែលបានជ្រើសក្នុងវិចិត្រសាល");
            A("Showing {0} students chosen in the Gallery", "បង្ហាញនិស្សិត {0} នាក់ ដែលបានជ្រើសក្នុងវិចិត្រសាល");
            A("Show every student in the list again (Esc in the search box)",
                "បង្ហាញនិស្សិតទាំងអស់ក្នុងបញ្ជីម្ដងទៀត (Esc ក្នុងប្រអប់ស្វែងរក)");
            A("None of the students chosen in the Gallery are in this list.",
                "គ្មាននិស្សិតដែលបានជ្រើសក្នុងវិចិត្រសាល នៅក្នុងបញ្ជីនេះទេ។");
            A("Click  Show all  to see every student.", "ចុច  បង្ហាញទាំងអស់  ដើម្បីមើលនិស្សិតទាំងអស់។");
            A("Showing the student chosen in the Gallery. Click Show all to see every student.",
                "កំពុងបង្ហាញនិស្សិតដែលបានជ្រើសក្នុងវិចិត្រសាល។ ចុច បង្ហាញទាំងអស់ ដើម្បីមើលនិស្សិតទាំងអស់។");
            A("Showing the {0} students chosen in the Gallery, all selected. Click Show all to see every student.",
                "កំពុងបង្ហាញនិស្សិត {0} នាក់ ដែលបានជ្រើសក្នុងវិចិត្រសាល ហើយបានជ្រើសទាំងអស់។ ចុច បង្ហាញទាំងអស់ ដើម្បីមើលនិស្សិតទាំងអស់។");
            A("from {0}", "ពី {0}");
            A("in the list", "ក្នុងបញ្ជី");
            A("{0} tray load(s)  ·  {1} side", "ដាក់ថាស {0} ដង  ·  ផ្នែក{1}");

            // ---- Student search (on the card preview) and Student card ----
            A("Search a student by ID, name or faculty…   (Ctrl+F)", "ស្វែងរកនិស្សិតតាមអត្តលេខ ឈ្មោះ ឬមហាវិទ្យាល័យ…   (Ctrl+F)");
            A("Previous student (Page Up in the search box)", "និស្សិតមុន (Page Up ក្នុងប្រអប់ស្វែងរក)");
            A("Next student (Page Down in the search box)", "និស្សិតបន្ទាប់ (Page Down ក្នុងប្រអប់ស្វែងរក)");
            A("No students loaded yet. Click Import… to load them (Ctrl+L).",
                "មិនទាន់មាននិស្សិតត្រូវបានផ្ទុកនៅឡើយទេ។ ចុច នាំចូល… ដើម្បីផ្ទុក (Ctrl+L)។");
            A("{0} more — type more of the ID or name to narrow the list.",
                "{0} នាក់ទៀត — វាយអត្តលេខ ឬឈ្មោះបន្ថែម ដើម្បីបង្រួមបញ្ជី។");
            A("No student selected.", "មិនទាន់ជ្រើសនិស្សិតទេ។");
            A("Search for a student above the card preview (Ctrl+F) to see their card,",
                "ស្វែងរកនិស្សិតនៅខាងលើការមើលកាតជាមុន (Ctrl+F) ដើម្បីមើលកាតរបស់គាត់");
            A("or click  View List  to choose students in the Gallery.", "ឬចុច  មើលបញ្ជី  ដើម្បីជ្រើសនិស្សិតក្នុងវិចិត្រសាល។");
            A("Load the student list from Excel, CSV, the web API or SQL Server (Ctrl+L)",
                "ផ្ទុកបញ្ជីនិស្សិតពី Excel, CSV, web API ឬ SQL Server (Ctrl+L)");
            A("View List", "មើលបញ្ជី");
            A("View every loaded student with photo, details and print status; choose up to 2 to print (Ctrl+G)",
                "មើលនិស្សិតដែលបានផ្ទុកទាំងអស់ ជាមួយរូបថត ព័ត៌មានលម្អិត និងស្ថានភាពបោះពុម្ព ហើយជ្រើសរហូតដល់ 2 នាក់ដើម្បីបោះពុម្ព (Ctrl+G)");
            // Selected students (at most 2: one per tray slot)
            A("1 / 2 selected — pick one more student for slot 2.", "បានជ្រើស 1 / 2 — ជ្រើសនិស្សិតម្នាក់ទៀតសម្រាប់រន្ធ 2។");
            A("2 / 2 selected — the most for one tray load. Remove one (✕) to pick another.",
                "បានជ្រើស 2 / 2 — ច្រើនបំផុតសម្រាប់ការដាក់ថាសម្ដង។ ដកម្នាក់ចេញ (✕) ដើម្បីជ្រើសម្នាក់ទៀត។");
            A("Not added: {0}. 2 students are already selected (one per tray slot) — remove one (✕) first.",
                "មិនបានបន្ថែម៖ {0}។ បានជ្រើសនិស្សិត 2 នាក់រួចហើយ (ម្នាក់ក្នុងមួយរន្ធ) — សូមដកម្នាក់ចេញ (✕) ជាមុនសិន។");
            A("Slot 1 · click to show this student", "រន្ធ 1 · ចុចដើម្បីបង្ហាញនិស្សិតនេះ");
            A("Slot 2 · click to show this student", "រន្ធ 2 · ចុចដើម្បីបង្ហាញនិស្សិតនេះ");
            A("Slot 1 · click to show this student · double-click to enlarge", "រន្ធ 1 · ចុចដើម្បីបង្ហាញនិស្សិតនេះ · ចុចពីរដងដើម្បីពង្រីក");
            A("Slot 2 · click to show this student · double-click to enlarge", "រន្ធ 2 · ចុចដើម្បីបង្ហាញនិស្សិតនេះ · ចុចពីរដងដើម្បីពង្រីក");
            A("Remove from the selection", "ដកចេញពីការជ្រើស");
            A("Clear", "សម្អាត");
            A("Remove both students from the selection", "ដកនិស្សិតទាំងពីរចេញពីការជ្រើស");
            A("{0} students", "និស្សិត {0} នាក់");
            A("{0} of {1} students", "និស្សិត {0} ក្នុងចំណោម {1} នាក់");
            A("{0}  ·  {1} selected", "{0}  ·  បានជ្រើស {1}");
            A("No students loaded yet.", "មិនទាន់មាននិស្សិតត្រូវបានផ្ទុកនៅឡើយទេ។");
            A("Click  Import Students…  in the sidebar (Ctrl+L) to load them from Excel, CSV, API or SQL Server.",
                "ចុច  នាំចូលនិស្សិត…  នៅរបារចំហៀង (Ctrl+L) ដើម្បីផ្ទុកពី Excel, CSV, API ឬ SQL Server។");
            A("No list yet? Save an Excel template there to fill in.", "មិនទាន់មានបញ្ជី? រក្សាទុកគំរូ Excel នៅទីនោះដើម្បីបំពេញ។");
            A("New to Card Studio? Press F1 for the user guide.", "ទើបប្រើ Card Studio ដំបូង? ចុច F1 ដើម្បីមើលការណែនាំប្រើប្រាស់។");
            A("No students match “{0}”.", "គ្មាននិស្សិតត្រូវនឹង “{0}” ទេ។");
            A("Press Esc to clear the search.", "ចុច Esc ដើម្បីសម្អាតការស្វែងរក។");
            // Student list columns
            A("ID", "អត្តលេខ");
            A("Name (KH)", "ឈ្មោះ (ខ្មែរ)");
            A("Name (EN)", "ឈ្មោះ (ឡាតាំង)");
            A("Sex", "ភេទ");
            A("Date of birth", "ថ្ងៃខែឆ្នាំកំណើត");
            A("Faculty", "មហាវិទ្យាល័យ");
            A("Level", "កម្រិត");
            A("Gen.", "ជំនាន់");
            A("Valid until", "សុពលភាពដល់");
            A("QR data", "ទិន្នន័យ QR");

            // ---- Card preview ----
            A("Card preview", "មើលកាតជាមុន");
            A("Select a student", "ជ្រើសនិស្សិតម្នាក់");
            A("Search for a student to see the card", "ស្វែងរកនិស្សិតដើម្បីមើលកាត");
            A("No students loaded yet", "មិនទាន់មាននិស្សិតត្រូវបានផ្ទុក");
            A("Back side", "ផ្នែកខាងក្រោយ");
            A("Click to print this side · double-click to enlarge", "ចុចដើម្បីបោះពុម្ពផ្នែកនេះ · ចុចពីរដងដើម្បីពង្រីក");
            A("Grid", "ក្រឡា");
            A("Grid ✓", "ក្រឡា ✓");
            A("Show / hide a millimetre grid on the front and back previews (not printed)",
                "បង្ហាញ / លាក់ក្រឡាមីលីម៉ែត្រលើការមើលជាមុនផ្នែកមុខ និងក្រោយ (មិនបោះពុម្ព)");
            A("Enlarge", "ពង្រីក");
            A("3D View", "មើល 3D");
            A("Turn the card in 3D: drag to rotate it, flip between front and back (Shift+F11)",
                "បង្វិលកាតជា 3D៖ អូសដើម្បីបង្វិល ត្រឡប់រវាងផ្នែកមុខ និងក្រោយ (Shift+F11)");

            // ---- Print setup: Printer ----
            A("Print setup", "ការកំណត់បោះពុម្ព");
            A("RFID", "RFID");
            A("Printer = driver and checks · Tray = slot positions and card orientation · " +
              "RFID = save the printed card's chip number to the RFID CSV file",
                "ម៉ាស៊ីនបោះពុម្ព = driver និងការពិនិត្យ · ថាស = ទីតាំងរន្ធ និងទិសកាត · " +
                "RFID = រក្សាទុកលេខឈីបរបស់កាតដែលបានបោះពុម្ពទៅឯកសារ RFID CSV");
            A("Show all", "បង្ហាញទាំងអស់");
            A("List every installed printer, not only card printers",
                "បង្ហាញម៉ាស៊ីនបោះពុម្ពដែលបានដំឡើងទាំងអស់ មិនមែនតែម៉ាស៊ីនបោះពុម្ពកាតទេ");
            A("Driver Setup…", "ការកំណត់ Driver…");
            A("Epson printers: paper source, paper type and quality (Ctrl+Shift+E)",
                "ម៉ាស៊ីនបោះពុម្ព Epson៖ ប្រភពក្រដាស ប្រភេទក្រដាស និងគុណភាព (Ctrl+Shift+E)");
            A("Check Printer", "ពិនិត្យម៉ាស៊ីនបោះពុម្ព");
            A("Show the page setup and any problems found", "បង្ហាញការកំណត់ទំព័រ និងបញ្ហាដែលរកឃើញ");
            A("Test Print", "បោះពុម្ពសាកល្បង");
            A("Print a calibration card to tune slot X / Y", "បោះពុម្ពកាតតម្រឹម ដើម្បីកែតម្រូវ X / Y របស់រន្ធ");
            A("Print Queue…", "ជួរបោះពុម្ព…");
            A("No card printer selected.", "មិនទាន់ជ្រើសម៉ាស៊ីនបោះពុម្ពកាត។");

            // ---- Print setup: Tray ----
            A("Slot 1", "រន្ធ 1");
            A("Slot 2", "រន្ធ 2");
            A("Per tray load", "ក្នុងមួយថាស");
            A("Auto = 2 cards per tray load when 2 or more students are selected and slot 2 is set up; otherwise slot 1 only.\n" +
              "1 = always slot 1 only.   2 = always slot 1 + slot 2.",
                "ស្វ័យប្រវត្តិ = កាត 2 ក្នុងមួយថាស នៅពេលជ្រើសនិស្សិត 2 នាក់ឡើងទៅ ហើយរន្ធ 2 ត្រូវបានកំណត់ បើមិនដូច្នោះទេ តែរន្ធ 1។\n" +
                "1 = តែរន្ធ 1 ជានិច្ច។   2 = រន្ធ 1 + រន្ធ 2 ជានិច្ច។");
            A("Card on tray", "កាតលើថាស");
            A("Horizontal", "ផ្ដេក");
            A("Vertical (turn right 90°)", "បញ្ឈរ (បង្វិលស្ដាំ 90°)");
            A("Vertical (turn left 90°)", "បញ្ឈរ (បង្វិលឆ្វេង 90°)");
            A("How the card lies in the tray pocket.\n" +
              "Vertical = the long side of the card runs in the feed direction (54 x 85.6 mm on the page).\n" +
              "If the card comes out upside down, pick the other Vertical option.",
                "របៀបដែលកាតដាក់ក្នុងរន្ធថាស។\n" +
                "បញ្ឈរ = ជ្រុងវែងរបស់កាតស្របនឹងទិសបញ្ចូល (54 x 85.6 mm លើទំព័រ)។\n" +
                "ប្រសិនបើកាតចេញមកបញ្ច្រាស សូមជ្រើសជម្រើសបញ្ឈរមួយទៀត។");
            A("Rotate 180°", "បង្វិល 180°");
            A("Rotate the front side 180° when printing", "បង្វិលផ្នែកមុខ 180° ពេលបោះពុម្ព");
            A("Rotate the back side 180° when printing", "បង្វិលផ្នែកក្រោយ 180° ពេលបោះពុម្ព");
            A("Save slot positions and print options", "រក្សាទុកទីតាំងរន្ធ និងជម្រើសបោះពុម្ព");
            A("Slot 1 X: distance from the left edge of the page (mm)", "រន្ធ 1 X៖ ចម្ងាយពីគែមឆ្វេងនៃទំព័រ (mm)");
            A("Slot 1 Y: distance from the top edge of the page (mm)", "រន្ធ 1 Y៖ ចម្ងាយពីគែមខាងលើនៃទំព័រ (mm)");
            A("Slot 2 X: distance from the left edge of the page (mm)", "រន្ធ 2 X៖ ចម្ងាយពីគែមឆ្វេងនៃទំព័រ (mm)");
            A("Slot 2 Y: distance from the top edge of the page (mm)", "រន្ធ 2 Y៖ ចម្ងាយពីគែមខាងលើនៃទំព័រ (mm)");
            A("Prints", "បោះពុម្ព");
            A("Not used now", "មិនប្រើពេលនេះ");
            A("Problem", "មានបញ្ហា");
            A("Page {0:0.#} × {1:0.#} mm", "ទំព័រ {0:0.#} × {1:0.#} mm");
            A("Page size unknown", "មិនស្គាល់ទំហំទំព័រ");
            A("Where slot 1 and slot 2 land on the printer page. Click a slot to edit its X / Y.",
                "ទីតាំងរន្ធ 1 និងរន្ធ 2 នៅលើទំព័របោះពុម្ព។ ចុចលើរន្ធមួយ ដើម្បីកែ X / Y របស់វា។");
            A("{0} card(s) → {1} tray load(s)", "កាត {0} → ដាក់ថាស {1} ដង");
            A("{0} card(s) → {1} tray load(s), 2 per load", "កាត {0} → ដាក់ថាស {1} ដង, 2 ក្នុងមួយដង");
            A("{0} card(s) → {1} tray load(s), slot 1 only — slot 2 not usable",
                "កាត {0} → ដាក់ថាស {1} ដង, តែរន្ធ 1 — រន្ធ 2 មិនអាចប្រើបាន");

            // ---- Print setup: RFID ----
            A("Tap the card on the reader…", "ប៉ះកាតលើឧបករណ៍អាន…");
            A("RFID card number (UID). A USB reader types it here and presses Enter, which uploads it.",
                "លេខកាត RFID (UID)។ ឧបករណ៍អាន USB វាយលេខនៅទីនេះ ហើយចុច Enter ដែលផ្ទុកវាឡើង។");
            A("Upload", "ផ្ទុកឡើង");
            A("Saving…", "កំពុងរក្សាទុក…");
            A("Save this RFID card number for the selected student in the RFID CSV file (Enter)",
                "រក្សាទុកលេខកាត RFID នេះសម្រាប់និស្សិតដែលបានជ្រើស ក្នុងឯកសារ RFID CSV (Enter)");
            A("List…", "បញ្ជី…");
            A("Every saved card number, and loaded students who have no card yet (Ctrl+Shift+R)",
                "លេខកាតដែលបានរក្សាទុកទាំងអស់ និងនិស្សិតដែលបានផ្ទុកតែមិនទាន់មានកាត (Ctrl+Shift+R)");
            A("Choose where the RFID CSV file is kept, e.g. a shared network folder used by several PCs",
                "ជ្រើសកន្លែងរក្សាទុកឯកសារ RFID CSV ឧ. ថតបណ្ដាញរួមដែលកុំព្យូទ័រច្រើនប្រើ");
            A("Select the student whose card you printed", "ជ្រើសនិស្សិតដែលអ្នកបានបោះពុម្ពកាត");
            A("Printed {0}  ·  tap the card on the reader", "បានបោះពុម្ព {0}  ·  ប៉ះកាតលើឧបករណ៍អាន");
            A("Not printed yet — print the card first", "មិនទាន់បោះពុម្ព — សូមបោះពុម្ពកាតជាមុនសិន");
            A("Saves to {0}  (app data)", "រក្សាទុកទៅ {0}  (ទិន្នន័យកម្មវិធី)");
            A("Saves to {0}  ·  {1}", "រក្សាទុកទៅ {0}  ·  {1}");
            A("RFID CSV file: {0}", "ឯកសារ RFID CSV៖ {0}");
            A("✓ {0}: replaced {1} with {2}  ({3})", "✓ {0}៖ បានជំនួស {1} ដោយ {2}  ({3})");
            A("✓ {0}: already saved: {1}  ({2})", "✓ {0}៖ បានរក្សាទុករួចហើយ៖ {1}  ({2})");
            A("✓ {0}: saved {1}  ({2})", "✓ {0}៖ បានរក្សាទុក {1}  ({2})");
            A("✓ {0}: replaced {1} with {2}  ({3})  ·  removed from the list",
                "✓ {0}៖ បានជំនួស {1} ដោយ {2}  ({3})  ·  បានដកចេញពីបញ្ជី");
            A("✓ {0}: already saved: {1}  ({2})  ·  removed from the list",
                "✓ {0}៖ បានរក្សាទុករួចហើយ៖ {1}  ({2})  ·  បានដកចេញពីបញ្ជី");
            A("✓ {0}: saved {1}  ({2})  ·  removed from the list", "✓ {0}៖ បានរក្សាទុក {1}  ({2})  ·  បានដកចេញពីបញ្ជី");
            A("Not saved — kept card {0} for {1}.", "មិនបានរក្សាទុក — រក្សាកាត {0} សម្រាប់ {1}។");
            A("Not saved — card {0} belongs to student {1}.", "មិនបានរក្សាទុក — កាត {0} ជារបស់និស្សិត {1}។");
            A("Not saved — {0}", "មិនបានរក្សាទុក — {0}");
            A("“{0}” is not an RFID card number.", "“{0}” មិនមែនជាលេខកាត RFID ទេ។");
        }

        /// <summary>Status bar, message boxes and the Import from API dialog of the main window.</summary>
        private static void MainWindowMessages()
        {
            // ---- Settings and printers ----
            A("Settings saved.", "បានរក្សាទុកការកំណត់។");
            A("Settings imported.", "បាននាំចូលការកំណត់។");
            A("No card printer found ({0}). Install the Epson L8050 driver, or tick \"Show all\".",
                "រកមិនឃើញម៉ាស៊ីនបោះពុម្ពកាត ({0})។ សូមដំឡើង driver Epson L8050 ឬធីក \"បង្ហាញទាំងអស់\"។");
            A("Epson driver settings saved for {0}.", "បានរក្សាទុកការកំណត់ driver Epson សម្រាប់ {0}។");

            // ---- Import ----
            A("Import students", "នាំចូលនិស្សិត");
            A("Import students from Excel", "នាំចូលនិស្សិតពី Excel");
            A("Import students from API", "នាំចូលនិស្សិតពី API");
            A("Optional — leave empty if the API is public", "ស្រេចចិត្ត — ទុកទទេប្រសិនបើ API ជាសាធារណៈ");
            A("Show token", "បង្ហាញ token");
            A("Remember token on this PC (encrypted for this Windows user)",
                "ចងចាំ token លើកុំព្យូទ័រនេះ (អ៊ិនគ្រីបសម្រាប់អ្នកប្រើ Windows នេះ)");
            A("The API must return a JSON list of students (or CSV) using the CSV column names: StudentId, NameKh, NameEn, Gender, " +
              "DateOfBirth, Faculty, Level, Generation, ValidUntil, QrData.",
                "API ត្រូវផ្ដល់បញ្ជីនិស្សិតជា JSON (ឬ CSV) ដោយប្រើឈ្មោះជួរឈរ CSV៖ StudentId, NameKh, NameEn, Gender, " +
                "DateOfBirth, Faculty, Level, Generation, ValidUntil, QrData។");
            A("The token is sent as “Authorization: Bearer …”. Type “Header-Name: value” to use another header.",
                "Token ត្រូវបានផ្ញើជា “Authorization: Bearer …”។ វាយ “Header-Name: value” ដើម្បីប្រើ header ផ្សេង។");
            A("API address", "អាសយដ្ឋាន API");
            A("Access token", "Token ចូលប្រើ");
            A("Load Students", "ផ្ទុកនិស្សិត");
            A("Downloading students…  (Cancel stops the download)", "កំពុងទាញយកនិស្សិត…  (បោះបង់ ដើម្បីបញ្ឈប់ការទាញយក)");
            A("Missing data: the API returned no students ({0})", "ខ្វះទិន្នន័យ៖ API មិនបានផ្ដល់និស្សិតទេ ({0})");
            A("Loaded {0} student(s) from {1}", "បានផ្ទុកនិស្សិត {0} នាក់ពី {1}");
            A("Missing data: the Google Sheet has no students", "ខ្វះទិន្នន័យ៖ Google Sheet គ្មាននិស្សិតទេ");
            A("Missing data: the SQL query returned no students ({0})", "ខ្វះទិន្នន័យ៖ សំណួរ SQL មិនបានផ្ដល់និស្សិតទេ ({0})");
            A("Loaded {0} student(s) from SQL Server {1}", "បានផ្ទុកនិស្សិត {0} នាក់ពី SQL Server {1}");
            A("Save Excel template", "រក្សាទុកគំរូ Excel");
            A("Could not save the template:", "មិនអាចរក្សាទុកគំរូបានទេ៖");
            A("Excel template saved: {0}", "បានរក្សាទុកគំរូ Excel៖ {0}");
            A("Template saved.", "បានរក្សាទុកគំរូ។");
            A("Fill in the Students sheet (one student per row), save it, then click Import Students… → From Excel…",
                "បំពេញសន្លឹក Students (និស្សិតម្នាក់ក្នុងមួយជួរ) រក្សាទុក រួចចុច នាំចូលនិស្សិត… → ពី Excel…");
            A("Open it now?", "បើកវាឥឡូវនេះ?");

            // ---- Photos ----
            A("Folder with photos named {StudentId}.jpg / .png", "ថតដែលមានរូបថតដាក់ឈ្មោះ {StudentId}.jpg / .png");
            A("Photo folder: {0}", "ថតរូបថត៖ {0}");
            A("Downloaded {0} photo(s) from {1} to {2}", "បានទាញយករូបថត {0} ពី {1} ទៅ {2}");
            A("No photo folder is set yet (or it no longer exists).", "មិនទាន់កំណត់ថតរូបថតទេ (ឬវាលែងមានហើយ)។");
            A("Choose one now?", "ជ្រើសរើសមួយឥឡូវនេះ?");
            A("Opened photo folder: {0}", "បានបើកថតរូបថត៖ {0}");
            A("Search for a student first (Ctrl+F).", "សូមស្វែងរកនិស្សិតជាមុនសិន (Ctrl+F)។");
            A("Choose the photo folder first (Photo Folder… in the sidebar).", "សូមជ្រើសថតរូបថតជាមុនសិន (ថតរូបថត… នៅរបារចំហៀង)។");
            A("No photo found for {0}.", "រកមិនឃើញរូបថតសម្រាប់ {0} ទេ។");
            A("Expected {0}.jpg / .png in:", "ត្រូវមាន {0}.jpg / .png នៅក្នុង៖");
            A("Original photo restored: {0}", "បានស្ដាររូបថតដើម៖ {0}");
            A("Background removed: {0}  (original kept in _original)", "បានលុបផ្ទៃខាងក្រោយ៖ {0}  (រូបដើមរក្សាទុកក្នុង _original)");
            A("Missing in {0}: {1}", "ខ្វះនៅក្នុង {0}៖ {1}");

            // ---- Card design ----
            A("Card template: {0}. Preview and printing now use this design.",
                "គំរូកាត៖ {0}។ ការមើលជាមុន និងការបោះពុម្ពឥឡូវប្រើការរចនានេះ។");
            A("Card design “{0}” saved. Preview and printing use it.", "បានរក្សាទុកការរចនាកាត “{0}”។ ការមើលជាមុន និងការបោះពុម្ពប្រើវា។");
            A("Card designs saved. Printing uses “{0}”.", "បានរក្សាទុកការរចនាកាត។ ការបោះពុម្ពប្រើ “{0}”។");
            A("Card designs saved. Pick one in Card Template… to print with it.",
                "បានរក្សាទុកការរចនាកាត។ ជ្រើសមួយក្នុង គំរូកាត… ដើម្បីបោះពុម្ពជាមួយវា។");
            A("Card text saved.", "បានរក្សាទុកអត្ថបទលើកាត។");
            A("Preview error: {0}", "កំហុសមើលជាមុន៖ {0}");

            // ---- Printing ----
            A("Install the Epson L8050 driver, then choose it in the Printer list.",
                "សូមដំឡើង driver Epson L8050 រួចជ្រើសវាក្នុងបញ្ជីម៉ាស៊ីនបោះពុម្ព។");
            A("\"{0}\" is not a card printer.", "\"{0}\" មិនមែនជាម៉ាស៊ីនបោះពុម្ពកាតទេ។");
            A("Print to it anyway?", "នៅតែបោះពុម្ពទៅវាឬ?");
            A("The card may not print:", "កាតប្រហែលជាមិនបោះពុម្ពទេ៖");
            A("Print anyway?", "នៅតែបោះពុម្ពឬ?");
            A("Slots per tray load: {0} ({1})", "រន្ធក្នុងមួយថាស៖ {0} ({1})");
            A("Slots per tray load: {0}", "រន្ធក្នុងមួយថាស៖ {0}");
            A("2 cards when 2+ are selected", "កាត 2 នៅពេលជ្រើស 2 ឡើងទៅ");
            A("slot 1 only — {0}", "តែរន្ធ 1 — {0}");
            A("Printer: {0}", "ម៉ាស៊ីនបោះពុម្ព៖ {0}");
            A("No problems found.", "រកមិនឃើញបញ្ហាទេ។");
            A("Problems:", "បញ្ហា៖");
            A("Calibration job sent. Insert the tray when the printer light turns on.",
                "បានផ្ញើការងារតម្រឹម។ ដាក់ថាសនៅពេលភ្លើងម៉ាស៊ីនបោះពុម្ពភ្លឺ។");
            A("Red line = card edge, each black line = 1 mm.", "បន្ទាត់ក្រហម = គែមកាត បន្ទាត់ខ្មៅនីមួយៗ = 1 mm។");
            A("Lines cut off on the RIGHT → decrease X.   On the LEFT → increase X.",
                "បន្ទាត់ដាច់នៅខាងស្ដាំ → បន្ថយ X។   នៅខាងឆ្វេង → បង្កើន X។");
            A("Lines cut off at the BOTTOM → decrease Y.   At the TOP → increase Y.",
                "បន្ទាត់ដាច់នៅខាងក្រោម → បន្ថយ Y។   នៅខាងលើ → បង្កើន Y។");
            A("Search for a student first, or click View List to choose several students in the Gallery.",
                "សូមស្វែងរកនិស្សិតជាមុនសិន ឬចុច មើលបញ្ជី ដើម្បីជ្រើសនិស្សិតច្រើននាក់ក្នុងវិចិត្រសាល។");
            A("Wait until the previous card is finished and the tray is ejected.",
                "រង់ចាំរហូតដល់កាតមុនបោះពុម្ពរួច ហើយថាសត្រូវបានបញ្ចេញ។");
            A("Auto slots: printing 1 card per tray load.", "រន្ធស្វ័យប្រវត្តិ៖ បោះពុម្ពកាត 1 ក្នុងមួយថាស។");
            A("Side: {0}", "ផ្នែក៖ {0}");
            A("Slot 1: {0}", "រន្ធ 1៖ {0}");
            A("Slot 2: {0}", "រន្ធ 2៖ {0}");
            A("Click OK to send the job, then insert the tray when the printer light turns on.",
                "ចុច យល់ព្រម ដើម្បីផ្ញើការងារ រួចដាក់ថាសនៅពេលភ្លើងម៉ាស៊ីនបោះពុម្ពភ្លឺ។");
            A("Tray load {0} of {1}", "ថាសទី {0} ក្នុងចំណោម {1}");
            A("Printing cancelled.", "បានបោះបង់ការបោះពុម្ព។");
            A("Sent tray load {0} of {1} ({2}).", "បានផ្ញើថាសទី {0} ក្នុងចំណោម {1} ({2})។");
            A("Missing history: {0} card(s) printed, but the print history could not be saved ({1}).",
                "ខ្វះប្រវត្តិ៖ បានបោះពុម្ពកាត {0} ប៉ុន្តែមិនអាចរក្សាទុកប្រវត្តិបោះពុម្ពបានទេ ({1})។");
            A("Done: {0} card(s), {1} side. Tap each card on the RFID reader to save its number.",
                "រួចរាល់៖ កាត {0} ផ្នែក{1}។ ប៉ះកាតនីមួយៗលើឧបករណ៍អាន RFID ដើម្បីរក្សាទុកលេខរបស់វា។");

            // ---- Appearance, title, license, updates ----
            A("Switch to dark mode?", "ប្ដូរទៅរបៀបងងឹត?");
            A("Switch to light mode?", "ប្ដូរទៅរបៀបភ្លឺ?");
            A("  —  Update {0} available (Ctrl+U)", "  —  មានកំណែថ្មី {0} (Ctrl+U)");
            A("  —  License expires {0}", "  —  អាជ្ញាប័ណ្ណផុតកំណត់ {0}");
            A("License expires {0} ({1}): ask your administrator for a new serial, then enter it in Settings & Help → License….",
                "អាជ្ញាប័ណ្ណផុតកំណត់ {0} ({1})៖ សូមស្នើលេខសៀរៀលថ្មីពីអ្នកគ្រប់គ្រង រួចបញ្ចូលវានៅ ការកំណត់ និងជំនួយ → អាជ្ញាប័ណ្ណ…។");
            A("License: {0}.", "អាជ្ញាប័ណ្ណ៖ {0}។");
            A("Update available: Card Studio {0} — press Ctrl+U to install it.", "មានកំណែថ្មី៖ Card Studio {0} — ចុច Ctrl+U ដើម្បីដំឡើង។");

            // ---- RFID ----
            A("RFID CSV file", "ឯកសារ RFID CSV");
            A("Using the existing RFID file with {0} card(s):", "ប្រើឯកសារ RFID ដែលមានស្រាប់ ដែលមានកាត {0}៖");
            A("The {0} saved card(s) were copied to the new RFID file:", "កាត {0} ដែលបានរក្សាទុក ត្រូវបានចម្លងទៅឯកសារ RFID ថ្មី៖");
            A("The old file is kept:", "ឯកសារចាស់នៅតែរក្សាទុក៖");
            A("New RFID cards will be saved to:", "កាត RFID ថ្មីនឹងត្រូវរក្សាទុកទៅ៖");
            A("The file is created with the first card.", "ឯកសារនឹងត្រូវបង្កើតនៅពេលមានកាតដំបូង។");
            A("RFID file: {0}", "ឯកសារ RFID៖ {0}");
            A("Select the student whose card you printed, then tap the card again.",
                "ជ្រើសនិស្សិតដែលអ្នកបានបោះពុម្ពកាត រួចប៉ះកាតម្ដងទៀត។");
            A("Tap the card on the reader again, or type its number (letters A–F / digits, at least 4 characters).",
                "ប៉ះកាតលើឧបករណ៍អានម្ដងទៀត ឬវាយលេខរបស់វា (អក្សរ A–F / លេខ យ៉ាងហោចណាស់ 4 តួ)។");
            A("{0} has no printed card in the print history.", "{0} មិនមានកាតដែលបានបោះពុម្ពក្នុងប្រវត្តិបោះពុម្ពទេ។");
            A("Save RFID card {0} for this student anyway?", "នៅតែរក្សាទុកកាត RFID {0} សម្រាប់និស្សិតនេះឬ?");
            A("{0} already has RFID card {1}.", "{0} មានកាត RFID {1} រួចហើយ។");
            A("Replace it with {0}? (e.g. the card was reprinted)", "ជំនួសវាដោយ {0}? (ឧ. កាតត្រូវបានបោះពុម្ពឡើងវិញ)");
            A("Error: RFID card {0} is already saved for student {1}.", "កំហុស៖ កាត RFID {0} បានរក្សាទុករួចសម្រាប់និស្សិត {1}។");
            A("RFID card {0} is already saved for student {1}.", "កាត RFID {0} បានរក្សាទុករួចសម្រាប់និស្សិត {1}។");
            A("Nothing was changed. Check you tapped the right card for {0}.",
                "គ្មានអ្វីត្រូវបានផ្លាស់ប្ដូរទេ។ សូមពិនិត្យថាអ្នកបានប៉ះកាតត្រឹមត្រូវសម្រាប់ {0}។");
            A("RFID card {0} saved for {1}.", "បានរក្សាទុកកាត RFID {0} សម្រាប់ {1}។");
        }

        /// <summary>Settings &amp; Help page.</summary>
        private static void Hub()
        {
            A("Settings & Help", "ការកំណត់ និងជំនួយ");
            A("Appearance, language, folders, settings, printer driver, system info, updates and the user guide",
                "រូបរាង ភាសា ថត ការកំណត់ driver ម៉ាស៊ីនបោះពុម្ព ព័ត៌មានប្រព័ន្ធ កំណែថ្មី និងការណែនាំប្រើប្រាស់");
            A("Light Mode", "របៀបភ្លឺ");
            A("Dark Mode", "របៀបងងឹត");
            A("Switch back to the light colours. The app restarts to apply it; printed cards are not affected.",
                "ត្រឡប់ទៅពណ៌ភ្លឺវិញ។ កម្មវិធីនឹងបើកឡើងវិញ កាតដែលបោះពុម្ពមិនប៉ះពាល់ទេ។");
            A("Dark colours, easier on the eyes in a dim room. The app restarts to apply it; printed cards are not affected.",
                "ពណ៌ងងឹត ស្រួលភ្នែកក្នុងបន្ទប់ស្រអាប់។ កម្មវិធីនឹងបើកឡើងវិញ កាតដែលបោះពុម្ពមិនប៉ះពាល់ទេ។");
            A("Theme Color…", "ពណ៌ចម្បង…");
            A("Primary colour of buttons, tabs and highlights, now {0}. Pick a preset or any colour, with a live preview.",
                "ពណ៌ប៊ូតុង ផ្ទាំង និងការរំលេច ឥឡូវជា {0}។ ជ្រើសពណ៌ណាមួយ ដោយមើលផ្ទាល់។");
            A("All Settings…", "ការកំណត់ទាំងអស់…");
            A("Every setting of the app and this PC: printer, tray, files, API, card text, fonts. Export, import or back them up.",
                "រាល់ការកំណត់៖ ម៉ាស៊ីនបោះពុម្ព ថាស ឯកសារ API អត្ថបទកាត។ នាំចេញ នាំចូល ឬបម្រុងទុក។");
            A("Folders…", "ថត…");
            A("Every folder the app uses: photos, backups, app data, card images. Open, change or create them.",
                "រាល់ថតដែលកម្មវិធីប្រើ៖ រូបថត ការបម្រុងទុក ទិន្នន័យ។ បើក ប្ដូរ ឬបង្កើតវា។");
            A("Epson Driver Setup…", "Driver Epson…");
            A("Choose the Epson card printer and save its paper source, paper type and quality.",
                "ជ្រើសម៉ាស៊ីនបោះពុម្ព Epson ហើយរក្សាទុកប្រភពក្រដាស ប្រភេទក្រដាស និងគុណភាព។");
            A("System Info", "ព័ត៌មានប្រព័ន្ធ");
            A("Version, students loaded, cards printed, RFID cards, data files, printer, fonts and card images of this PC.",
                "កំណែ និស្សិត កាតបានបោះពុម្ព RFID ឯកសារ ម៉ាស៊ីនបោះពុម្ព និងពុម្ពអក្សររបស់កុំព្យូទ័រនេះ។");
            A("Help & About", "ជំនួយ និងអំពី");
            A("What Card Studio does, step-by-step guide, shortcuts and troubleshooting, in English or Khmer · ជាភាសាខ្មែរ.",
                "ការណែនាំមួយជំហានម្ដងៗ ផ្លូវកាត់ និងការដោះស្រាយបញ្ហា ជាភាសាខ្មែរ ឬអង់គ្លេស។");
            A("Check for Updates", "ពិនិត្យរកកំណែថ្មី");
            A("Version {0} is installed. Look for a newer version and install it; settings, print history and card images are kept.",
                "បានដំឡើងកំណែ {0}។ ស្វែងរក និងដំឡើងកំណែថ្មី ដោយរក្សាការកំណត់ និងប្រវត្តិ។");
            A("License…", "អាជ្ញាប័ណ្ណ…");
            A(". Enter a new serial number from your administrator to renew it.",
                "។ បញ្ចូលលេខសៀរៀលថ្មី ដើម្បីបន្តសុពលភាព។");
            A("Show Card Studio in English. The app restarts to apply it; printed cards are not affected.",
                "បង្ហាញ Card Studio ជាភាសាអង់គ្លេស។ កម្មវិធីនឹងបើកឡើងវិញ កាតដែលបោះពុម្ពមិនប៉ះពាល់ទេ។");
        }
    }
}
