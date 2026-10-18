namespace NPIC_CARD_APP
{
    // Import Students, Google Sheets, SQL Server and CSV Files pages.
    internal static partial class KhmerStrings
    {
        private static void Import()
        {
            // ---- Import Students ----
            A("Import Students", "នាំចូលនិស្សិត");
            A("Choose where the student list comes from", "ជ្រើសប្រភពនៃបញ្ជីនិស្សិត");
            A("No students loaded yet. Pick a source below to begin.", "មិនទាន់មាននិស្សិតទេ។ ជ្រើសប្រភពខាងក្រោមដើម្បីចាប់ផ្ដើម។");
            A("Current list: {0} student(s). Importing replaces it.", "បញ្ជីបច្ចុប្បន្ន៖ និស្សិត {0} នាក់។ ការនាំចូលនឹងជំនួសវា។");
            A("Current list: {0} student(s) from {1}. Importing replaces it.",
                "បញ្ជីបច្ចុប្បន្ន៖ និស្សិត {0} នាក់ពី {1}។ ការនាំចូលនឹងជំនួសវា។");
            A("From Excel…", "ពី Excel…");
            A("Load students from an Excel workbook (.xlsx, .xlsm or .xls). Use the Excel Template to get the right columns.",
                "ផ្ទុកនិស្សិតពីឯកសារ Excel (.xlsx, .xlsm ឬ .xls)។ ប្រើគំរូ Excel ដើម្បីមានជួរឈរត្រឹមត្រូវ។");
            A("From CSV…", "ពី CSV…");
            A("Load students from a UTF-8 CSV file with the columns StudentId, NameKh, NameEn, Gender, DateOfBirth…",
                "ផ្ទុកនិស្សិតពីឯកសារ CSV UTF-8 ដែលមានជួរឈរ StudentId, NameKh, NameEn, Gender, DateOfBirth…");
            A("From API…", "ពី API…");
            A("Download the students from the school's web API (JSON or CSV), with an optional access token.",
                "ទាញយកនិស្សិតពី web API របស់សាលា (JSON ឬ CSV) ជាមួយ token ចូលប្រើ (ស្រេចចិត្ត)។");
            A("SQL Server…", "SQL Server…");
            A("Read the students straight from a SQL Server table or query. The app only reads the database.",
                "អាននិស្សិតដោយផ្ទាល់ពីតារាង ឬសំណួរ SQL Server។ កម្មវិធីគ្រាន់តែអានមូលដ្ឋានទិន្នន័យប៉ុណ្ណោះ។");
            A("From Google Sheets…", "ពី Google Sheets…");
            A("Paste the link of a Google Sheet shared as “Anyone with the link”. Row 1 holds the same columns as the Excel template.",
                "បិទភ្ជាប់តំណ Google Sheet ដែលចែករំលែកជា “Anyone with the link”។ ជួរទី 1 មានជួរឈរដូចគំរូ Excel។");
            A("Save Excel Template…", "រក្សាទុកគំរូ Excel…");
            A("No list yet? Save an empty Excel file with the right columns, fill it in, then use From Excel…",
                "មិនទាន់មានបញ្ជី? រក្សាទុកឯកសារ Excel ទទេដែលមានជួរឈរត្រឹមត្រូវ បំពេញវា រួចប្រើ ពី Excel…");

            // ---- Google Sheets ----
            A("Google Sheets", "Google Sheets");
            A("Read the student list from a shared Google Sheet", "អានបញ្ជីនិស្សិតពី Google Sheet ដែលបានចែករំលែក");
            A("Connect a Google Sheet", "ភ្ជាប់ Google Sheet");
            A("1.  In Google Sheets click Share → General access → “Anyone with the link” (Viewer).",
                "1.  ក្នុង Google Sheets ចុច Share → General access → “Anyone with the link” (Viewer)។");
            A("2.  Open the tab with the students and copy the browser's address bar.",
                "2.  បើកផ្ទាំងដែលមាននិស្សិត ហើយចម្លងរបារអាសយដ្ឋានរបស់កម្មវិធីរុករក។");
            A("3.  Paste it above and click Connect.", "3.  បិទភ្ជាប់វានៅខាងលើ ហើយចុច ភ្ជាប់។");
            A("Row 1 holds the column names: StudentId, NameKh, NameEn, Gender, DateOfBirth, Faculty,",
                "ជួរទី 1 មានឈ្មោះជួរឈរ៖ StudentId, NameKh, NameEn, Gender, DateOfBirth, Faculty,");
            A("Level, Generation, ValidUntil, QrData — the same as the Excel template. The app only reads the sheet.",
                "Level, Generation, ValidUntil, QrData — ដូចគំរូ Excel។ កម្មវិធីគ្រាន់តែអានសន្លឹកប៉ុណ្ណោះ។");
            A("Paste the sheet link: https://docs.google.com/spreadsheets/d/…/edit#gid=0",
                "បិទភ្ជាប់តំណសន្លឹក៖ https://docs.google.com/spreadsheets/d/…/edit#gid=0");
            A("Connect (F5)", "ភ្ជាប់ (F5)");
            A("Open in Browser", "បើកក្នុងកម្មវិធីរុករក");
            A("Sheet link", "តំណសន្លឹក");
            A("Download the sheet and preview its students here. Nothing changes until you click Import.",
                "ទាញយកសន្លឹក ហើយមើលនិស្សិតរបស់វាជាមុននៅទីនេះ។ គ្មានអ្វីផ្លាស់ប្ដូររហូតដល់អ្នកចុច នាំចូល។");
            A("Open the sheet in the browser, e.g. to fix its sharing or edit the students",
                "បើកសន្លឹកក្នុងកម្មវិធីរុករក ឧ. ដើម្បីកែការចែករំលែក ឬកែនិស្សិត");
            A("Replace the student list of the main window with the students shown here",
                "ជំនួសបញ្ជីនិស្សិតនៃផ្ទាំងមេ ដោយនិស្សិតដែលបង្ហាញនៅទីនេះ");
            A("Not connected.", "មិនទាន់ភ្ជាប់។");
            A("Cancelled.", "បានបោះបង់។");
            A("The sheet has a StudentId column but no students yet.", "សន្លឹកមានជួរឈរ StudentId ប៉ុន្តែមិនទាន់មាននិស្សិតទេ។");
            A("Add them in Google Sheets, then click Connect again.", "បន្ថែមពួកគេក្នុង Google Sheets រួចចុច ភ្ជាប់ ម្ដងទៀត។");
            A("{0} without StudentId (red)", "{0} គ្មាន StudentId (ក្រហម)");
            A("{0} duplicate StudentId", "{0} StudentId ស្ទួន");
            A("✔  Connected {0:HH:mm} — {1} student(s){2}", "✔  បានភ្ជាប់ {0:HH:mm} — និស្សិត {1} នាក់{2}");
            A("The link was changed after the preview was read.", "តំណត្រូវបានផ្លាស់ប្ដូរ បន្ទាប់ពីបានអានការមើលជាមុន។");
            A("Import the students shown (from the previous link)? Choose No, then Connect, to read the new link.",
                "នាំចូលនិស្សិតដែលបង្ហាញ (ពីតំណមុន)? ជ្រើស ទេ រួច ភ្ជាប់ ដើម្បីអានតំណថ្មី។");
            A("(missing)", "(ខ្វះ)");
            A("Could not open the browser: {0}", "មិនអាចបើកកម្មវិធីរុករកបានទេ៖ {0}");
            A("Stop", "បញ្ឈប់");
            A("Downloading the sheet…  (Stop cancels)", "កំពុងទាញយកសន្លឹក…  (បញ្ឈប់ ដើម្បីបោះបង់)");
            A("Import {0} Student(s)", "នាំចូលនិស្សិត {0} នាក់");

            // ---- SQL Server ----
            A("Connect SQL Server", "ភ្ជាប់ SQL Server");
            A("Load the student list straight from the school's SQL Server database",
                "ផ្ទុកបញ្ជីនិស្សិតដោយផ្ទាល់ពីមូលដ្ឋានទិន្នន័យ SQL Server របស់សាលា");
            A("localhost\\SQLEXPRESS  or  192.168.1.10,1433", "localhost\\SQLEXPRESS  ឬ  192.168.1.10,1433");
            A("Database name, e.g. NPIC", "ឈ្មោះមូលដ្ឋានទិន្នន័យ ឧ. NPIC");
            A("Windows Authentication", "Windows Authentication");
            A("SQL Server Authentication", "SQL Server Authentication");
            A("Login name, e.g. sa", "ឈ្មោះចូល ឧ. sa");
            A("Remember password (encrypted for this Windows user)", "ចងចាំពាក្យសម្ងាត់ (អ៊ិនគ្រីបសម្រាប់អ្នកប្រើ Windows នេះ)");
            A("Encrypt connection", "អ៊ិនគ្រីបការតភ្ជាប់");
            A("Trust server certificate", "ទុកចិត្តវិញ្ញាបនបត្រម៉ាស៊ីនមេ");
            A("Type a table name (dbo.Students) or one SELECT query. Columns use the CSV names: StudentId, NameKh, NameEn, " +
              "Gender, DateOfBirth, Faculty, Level, Generation, ValidUntil, QrData — rename others with AS. " +
              "The app only reads; it never changes the database.",
                "វាយឈ្មោះតារាង (dbo.Students) ឬសំណួរ SELECT មួយ។ ជួរឈរប្រើឈ្មោះ CSV៖ StudentId, NameKh, NameEn, " +
                "Gender, DateOfBirth, Faculty, Level, Generation, ValidUntil, QrData — ប្ដូរឈ្មោះផ្សេងទៀតដោយ AS។ " +
                "កម្មវិធីគ្រាន់តែអាន វាមិនដែលផ្លាស់ប្ដូរមូលដ្ឋានទិន្នន័យទេ។");
            A("Server", "ម៉ាស៊ីនមេ");
            A("Database", "មូលដ្ឋានទិន្នន័យ");
            A("Authentication", "ការផ្ទៀងផ្ទាត់");
            A("User name", "ឈ្មោះអ្នកប្រើ");
            A("Password", "ពាក្យសម្ងាត់");
            A("Security", "សុវត្ថិភាព");
            A("Table or query", "តារាង ឬសំណួរ");
            A("Test Connection", "សាកល្បងការតភ្ជាប់");
            A("Loading students…  (Close stops the query)", "កំពុងផ្ទុកនិស្សិត…  (បិទ ដើម្បីបញ្ឈប់សំណួរ)");
            A("Connecting…", "កំពុងតភ្ជាប់…");
            A("✔  {0} student(s) read.", "✔  បានអាននិស្សិត {0} នាក់។");
            A("✔  Connected — {0}", "✔  បានភ្ជាប់ — {0}");
            A("SQL Server {0}  ·  database {1}", "SQL Server {0}  ·  មូលដ្ឋានទិន្នន័យ {1}");

            // ---- CSV Files ----
            A("CSV Files", "ឯកសារ CSV");
            A("Student lists in one folder — check, preview, create, rename, delete or load them",
                "បញ្ជីនិស្សិតក្នុងថតតែមួយ — ពិនិត្យ មើលជាមុន បង្កើត ប្ដូរឈ្មោះ លុប ឬផ្ទុកវា");
            A("Show in Explorer", "បង្ហាញក្នុង Explorer");
            A("Change Folder…", "ប្ដូរថត…");
            A("Search files… (Ctrl+F)", "ស្វែងរកឯកសារ… (Ctrl+F)");
            A("New…", "ថ្មី…");
            A("Add Files…", "បន្ថែមឯកសារ…");
            A("Edit", "កែ");
            A("Duplicate", "ចម្លងមួយទៀត");
            A("Create an empty student CSV with the standard columns (Ctrl+N)", "បង្កើត CSV និស្សិតទទេ ដែលមានជួរឈរស្តង់ដារ (Ctrl+N)");
            A("Copy CSV files from elsewhere into this folder (you can also drop them on the list)",
                "ចម្លងឯកសារ CSV ពីកន្លែងផ្សេងចូលថតនេះ (អ្នកក៏អាចទម្លាក់វាលើបញ្ជីបានដែរ)");
            A("Open the file in Excel, or the program Windows uses for .csv, to edit it (Ctrl+E)",
                "បើកឯកសារក្នុង Excel ឬកម្មវិធីដែល Windows ប្រើសម្រាប់ .csv ដើម្បីកែវា (Ctrl+E)");
            A("Rename the selected file (F2)", "ប្ដូរឈ្មោះឯកសារដែលបានជ្រើស (F2)");
            A("Make a copy of the selected file in this folder", "ចម្លងឯកសារដែលបានជ្រើសមួយទៀតក្នុងថតនេះ");
            A("Move the selected file to the Recycle Bin (Del)", "ផ្លាស់ឯកសារដែលបានជ្រើសទៅធុងសំរាម (Del)");
            A("Choose the folder this page lists", "ជ្រើសថតដែលទំព័រនេះបង្ហាញ");
            A("Open the folder in Windows Explorer with the selected file highlighted",
                "បើកថតក្នុង Windows Explorer ដោយរំលេចឯកសារដែលបានជ្រើស");
            A("loaded", "បានផ្ទុក");
            A("The student list in the main window was loaded from this file.", "បញ្ជីនិស្សិតក្នុងផ្ទាំងមេ ត្រូវបានផ្ទុកពីឯកសារនេះ។");
            A("No CSV folder is chosen yet.", "មិនទាន់ជ្រើសថត CSV ទេ។");
            A("Click Change Folder… and pick the folder with your student CSV files.", "ចុច ប្ដូរថត… ហើយជ្រើសថតដែលមានឯកសារ CSV និស្សិតរបស់អ្នក។");
            A("This folder was not found:", "រកមិនឃើញថតនេះទេ៖");
            A("Click Change Folder… to pick it again.", "ចុច ប្ដូរថត… ដើម្បីជ្រើសវាម្ដងទៀត។");
            A("There are no CSV files in this folder yet.", "មិនទាន់មានឯកសារ CSV ក្នុងថតនេះទេ។");
            A("Click New… to create one, Add Files… to copy some in, or drop .csv files here.",
                "ចុច ថ្មី… ដើម្បីបង្កើតមួយ បន្ថែមឯកសារ… ដើម្បីចម្លងចូល ឬទម្លាក់ឯកសារ .csv នៅទីនេះ។");
            A("No file matches \"{0}\".", "គ្មានឯកសារត្រូវនឹង \"{0}\" ទេ។");
            A("Folder: not chosen", "ថត៖ មិនទាន់ជ្រើស");
            A("Folder: {0}", "ថត៖ {0}");
            A("{0} CSV file", "ឯកសារ CSV {0}");
            A("{0} CSV files", "ឯកសារ CSV {0}");
            A(", {0} ready to load", ", {0} រួចរាល់សម្រាប់ផ្ទុក");
            A("(not found)", "(រកមិនឃើញ)");
            A("folder of the last student file", "ថតនៃឯកសារនិស្សិតចុងក្រោយ");
            A("No folder", "គ្មានថត");
            A("  ·  {0} cannot be loaded (red)", "  ·  {0} មិនអាចផ្ទុកបាន (ក្រហម)");
            A("Select a file to preview it. Header row: {0}", "ជ្រើសឯកសារដើម្បីមើលវាជាមុន។ ជួរក្បាល៖ {0}");
            A("  ·  modified {0}", "  ·  កែប្រែ {0}");
            A("{0} column", "ជួរឈរ {0}");
            A("{0} columns", "ជួរឈរ {0}");
            A("{0} student", "និស្សិត {0} នាក់");
            A("{0} row", "{0} ជួរ");
            A("{0} rows", "{0} ជួរ");
            A("{0} StudentId", "StudentId {0}");
            A("{0} StudentIds", "StudentId {0}");
            A("Preview", "មើលជាមុន");
            A("Preview — cannot read {0}: {1}", "មើលជាមុន — មិនអាចអាន {0}៖ {1}");
            A("Preview — {0} is empty", "មើលជាមុន — {0} ទទេ");
            A("Preview — {0}", "មើលជាមុន — {0}");
            A("first {0} of {1} rows", "{0} ជួរដំបូង ក្នុងចំណោម {1}");
            A("  ·  no StudentId column", "  ·  គ្មានជួរឈរ StudentId");
            A("(no name)", "(គ្មានឈ្មោះ)");
            A("No StudentId: this card has no photo or ID", "គ្មាន StudentId៖ កាតនេះគ្មានរូបថត ឬអត្តលេខទេ");
            A("This StudentId appears more than once", "StudentId នេះលេចឡើងច្រើនជាងម្ដង");
            A("{0} cannot be loaded.", "{0} មិនអាចផ្ទុកបានទេ។");
            A("New CSV File", "ឯកសារ CSV ថ្មី");
            A("An empty student list with the standard columns", "បញ្ជីនិស្សិតទទេ ដែលមានជួរឈរស្តង់ដារ");
            A("Could not create the file:", "មិនអាចបង្កើតឯកសារបានទេ៖");
            A("{0} was created.", "{0} ត្រូវបានបង្កើត។");
            A("Open it now to fill in the students (one per row), then save it as CSV UTF-8?",
                "បើកវាឥឡូវនេះដើម្បីបំពេញនិស្សិត (ម្នាក់ក្នុងមួយជួរ) រួចរក្សាទុកជា CSV UTF-8?");
            A("Add CSV files to {0}", "បន្ថែមឯកសារ CSV ទៅ {0}");
            A("{0} already exists in this folder.", "{0} មានរួចហើយក្នុងថតនេះ។");
            A("Yes = replace it     No = keep both (the new one gets another name)     Cancel = skip it",
                "បាទ/ចាស = ជំនួសវា     ទេ = រក្សាទុកទាំងពីរ (ឯកសារថ្មីទទួលឈ្មោះផ្សេង)     បោះបង់ = រំលងវា");
            A("Add Files", "បន្ថែមឯកសារ");
            A("Could not copy {0}.", "មិនអាចចម្លង {0} បានទេ។");
            A("Rename", "ប្ដូរឈ្មោះ");
            A("Could not rename {0}.", "មិនអាចប្ដូរឈ្មោះ {0} បានទេ។");
            A("Is it open in Excel? Close it there and try again.", "តើវាកំពុងបើកក្នុង Excel ឬ? សូមបិទវានៅទីនោះ រួចព្យាយាមម្ដងទៀត។");
            A("Move {0} to the Recycle Bin?", "ផ្លាស់ {0} ទៅធុងសំរាម?");
            A("It holds {0}.", "វាមាន {0}។");
            A("The student list in the main window was loaded from this file. It stays loaded until you import another list.",
                "បញ្ជីនិស្សិតក្នុងផ្ទាំងមេ ត្រូវបានផ្ទុកពីឯកសារនេះ។ វានៅតែផ្ទុក រហូតដល់អ្នកនាំចូលបញ្ជីផ្សេង។");
            A("Could not delete {0}.", "មិនអាចលុប {0} បានទេ។");
            A("Folder with the student CSV files", "ថតដែលមានឯកសារ CSV និស្សិត");
            A("Could not open the folder.", "មិនអាចបើកថតបានទេ។");
            A("Copied to the clipboard:", "បានចម្លងទៅក្ដារតម្បៀតខ្ទាស់៖");
            A("Could not copy to the clipboard.", "មិនអាចចម្លងទៅក្ដារតម្បៀតខ្ទាស់បានទេ។");
            A("Choose the CSV folder first (Change Folder…).", "សូមជ្រើសថត CSV ជាមុនសិន (ប្ដូរថត…)។");
            A("Choose it again with Change Folder….", "ជ្រើសវាម្ដងទៀតដោយ ប្ដូរថត…។");
            A("Could not open {0}.", "មិនអាចបើក {0} បានទេ។");
            A("Is a program such as Excel installed for .csv files?", "តើមានកម្មវិធីដូចជា Excel ដំឡើងសម្រាប់ឯកសារ .csv ទេ?");
            A("A file named {0} already exists in this folder. Choose another name.",
                "ឯកសារឈ្មោះ {0} មានរួចហើយក្នុងថតនេះ។ សូមជ្រើសឈ្មោះផ្សេង។");
            A("In use by another program, or unreadable ({0})", "កំពុងប្រើដោយកម្មវិធីផ្សេង ឬមិនអាចអានបាន ({0})");
            A("✖ Cannot read", "✖ មិនអាចអាន");
            A("Empty file", "ឯកសារទទេ");
            A("✖ No StudentId column", "✖ គ្មានជួរឈរ StudentId");
            A("No students yet", "មិនទាន់មាននិស្សិត");
            A("{0} without ID", "{0} គ្មានអត្តលេខ");
            A("{0} duplicate ID", "{0} អត្តលេខស្ទួន");
            A("{0} duplicate IDs", "{0} អត្តលេខស្ទួន");
            A("✔ Ready", "✔ រួចរាល់");
            A("Cannot read the file: {0}", "មិនអាចអានឯកសារបានទេ៖ {0}");
            A("The file is empty. Click Edit to add the header row: {0}", "ឯកសារទទេ។ ចុច កែ ដើម្បីបន្ថែមជួរក្បាល៖ {0}");
            A("The first row has no 'StudentId' column, so the students cannot be loaded. Click Edit to fix the header.",
                "ជួរទីមួយគ្មានជួរឈរ 'StudentId' ដូច្នេះមិនអាចផ្ទុកនិស្សិតបានទេ។ ចុច កែ ដើម្បីកែជួរក្បាល។");
            A("Only the header row is there. Click Edit to add students (one per row).",
                "មានតែជួរក្បាលប៉ុណ្ណោះ។ ចុច កែ ដើម្បីបន្ថែមនិស្សិត (ម្នាក់ក្នុងមួយជួរ)។");
            A("{0} without a StudentId (highlighted in the preview)", "{0} គ្មាន StudentId (បានរំលេចក្នុងការមើលជាមុន)");
            A("{0} used more than once", "{0} ត្រូវបានប្រើច្រើនជាងម្ដង");
            A("missing columns: {0}", "ខ្វះជួរឈរ៖ {0}");
            A(" and {0} more", " និង {0} ទៀត");
            A("Ready to load — press Enter or double-click.", "រួចរាល់សម្រាប់ផ្ទុក — ចុច Enter ឬចុចពីរដង។");
            A("Can be loaded, but: {0}.", "អាចផ្ទុកបាន ប៉ុន្តែ៖ {0}។");
            A("Type a name for the file.", "សូមវាយឈ្មោះសម្រាប់ឯកសារ។");
            A("A file name cannot contain any of these characters:  \\ / : * ? \" < > |",
                "ឈ្មោះឯកសារមិនអាចមានតួអក្សរទាំងនេះទេ៖  \\ / : * ? \" < > |");
            A("File name  (.csv is added)", "ឈ្មោះឯកសារ  (.csv នឹងត្រូវបន្ថែម)");
        }
    }
}
