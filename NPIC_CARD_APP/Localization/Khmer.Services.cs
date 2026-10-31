namespace NPIC_CARD_APP
{
    // Messages from the services (Services\*.cs, Rendering\CardFonts.cs…) shown in message boxes and status lines.
    // Messages of several paragraphs are translated paragraph by paragraph, so each paragraph is its own entry.
    internal static partial class KhmerStrings
    {
        private static void Services()
        {
            // ---- Fonts ----
            A("These fonts are not installed, so the card will not match the design:", "ពុម្ពអក្សរទាំងនេះមិនទាន់បានដំឡើង ដូច្នេះកាតនឹងមិនដូចការរចនាទេ៖");
            A("Card Studio could not install them automatically from:", "Card Studio មិនអាចដំឡើងពួកវាដោយស្វ័យប្រវត្តិពី៖");
            A("Install them by hand (right-click the .ttf → Install for all users), then restart the app.",
                "ដំឡើងវាដោយដៃ (ចុចកណ្ដុរស្ដាំលើ .ttf → Install for all users) រួចបើកកម្មវិធីឡើងវិញ។");
            A("Missing fonts", "ខ្វះពុម្ពអក្សរ");

            // ---- Card Studio Admin (MainForm.Admin.cs) ----
            A("Message from the administrator", "សារពីអ្នកគ្រប់គ្រង");
            A("Update requested", "អ្នកគ្រប់គ្រងស្នើឱ្យធ្វើបច្ចុប្បន្នភាព");
            A("Update requested by the administrator: press Ctrl+U to check for updates.",
                "អ្នកគ្រប់គ្រងស្នើឱ្យធ្វើបច្ចុប្បន្នភាព៖ ចុច Ctrl+U ដើម្បីពិនិត្យកំណែថ្មី។");
            A("License renewed", "អាជ្ញាប័ណ្ណត្រូវបានបន្ត");
            A("Phones must pair again", "ទូរស័ព្ទត្រូវភ្ជាប់ម្ដងទៀត");
            A("Phones disconnected by the administrator: pair them again on the Mobile App page.",
                "អ្នកគ្រប់គ្រងបានផ្ដាច់ទូរស័ព្ទ៖ សូមភ្ជាប់វាម្ដងទៀតនៅទំព័រ Mobile App។");

            // ---- Student lists (CSV, Excel, Google Sheets, API, SQL Server) ----
            A("{0} must contain a 'StudentId' column.", "{0} ត្រូវតែមានជួរឈរ 'StudentId'។");
            A("The first row of the Excel sheet", "ជួរទីមួយនៃសន្លឹក Excel");
            A("The Google Sheet's first row", "ជួរទីមួយនៃ Google Sheet");
            A("Old Excel 97-2003 (.xls) files are not supported.", "មិនគាំទ្រឯកសារ Excel 97-2003 (.xls) ចាស់ទេ។");
            A("Open the file in Excel, choose File > Save As > \"Excel Workbook (*.xlsx)\", then import the new file.",
                "បើកឯកសារក្នុង Excel ជ្រើស File > Save As > \"Excel Workbook (*.xlsx)\" រួចនាំចូលឯកសារថ្មី។");
            A("The workbook has no worksheets.", "ឯកសារ Excel គ្មានសន្លឹកកិច្ចការទេ។");
            A("This file is not a valid .xlsx workbook.", "ឯកសារនេះមិនមែនជា .xlsx ត្រឹមត្រូវទេ។");
            A("If it is password-protected, remove the password in Excel first; if it is an old .xls file, save it as \"Excel Workbook (*.xlsx)\".",
                "ប្រសិនបើវាមានពាក្យសម្ងាត់ សូមដកពាក្យសម្ងាត់ក្នុង Excel ជាមុន ប្រសិនបើវាជាឯកសារ .xls ចាស់ សូមរក្សាទុកវាជា \"Excel Workbook (*.xlsx)\"។");
            A("This file is not a valid .xlsx workbook (no workbook part found).", "ឯកសារនេះមិនមែនជា .xlsx ត្រឹមត្រូវទេ (រកមិនឃើញផ្នែក workbook)។");
            A("Paste the link of the Google Sheet.", "សូមបិទភ្ជាប់តំណ Google Sheet។");
            A("This is not a Google Sheets link.", "នេះមិនមែនជាតំណ Google Sheets ទេ។");
            A("Open the sheet in the browser and copy the address, e.g.", "បើកសន្លឹកក្នុងកម្មវិធីរុករក ហើយចម្លងអាសយដ្ឋាន ឧ.");
            A("Google Sheets did not answer within {0} seconds.", "Google Sheets មិនបានឆ្លើយតបក្នុងរយៈពេល {0} វិនាទីទេ។");
            A("Google Sheets could not find this sheet. Check the link.", "Google Sheets រកមិនឃើញសន្លឹកនេះទេ។ សូមពិនិត្យតំណ។");
            A("Google Sheets could not find this tab.", "Google Sheets រកមិនឃើញផ្ទាំងនេះទេ។");
            A("Open the tab with the students and copy the address again (it ends in #gid=…).",
                "បើកផ្ទាំងដែលមាននិស្សិត ហើយចម្លងអាសយដ្ឋានម្ដងទៀត (វាបញ្ចប់ដោយ #gid=…)។");
            A("Google Sheets answered {0} {1}. Try again later.", "Google Sheets បានឆ្លើយតប {0} {1}។ សូមព្យាយាមម្ដងទៀតពេលក្រោយ។");
            A("The Google Sheet is private, so the app cannot read it.", "Google Sheet ជាឯកជន ដូច្នេះកម្មវិធីមិនអាចអានវាបានទេ។");
            A("In Google Sheets click Share → General access → “Anyone with the link” (Viewer), then try again.",
                "ក្នុង Google Sheets ចុច Share → General access → “Anyone with the link” (Viewer) រួចព្យាយាមម្ដងទៀត។");
            A("Or use File → Share → Publish to web, and paste that link.", "ឬប្រើ File → Share → Publish to web ហើយបិទភ្ជាប់តំណនោះ។");
            A("Enter the full API address, starting with http:// or https://", "បញ្ចូលអាសយដ្ឋាន API ពេញលេញ ដោយចាប់ផ្ដើមដោយ http:// ឬ https://");
            A("The API returned more than {0} pages. Ask for bigger pages (e.g. ?per_page=500).",
                "API បានផ្ដល់លើសពី {0} ទំព័រ។ សូមស្នើទំព័រធំជាងនេះ (ឧ. ?per_page=500)។");
            A("The server returned a web page instead of student data.", "ម៉ាស៊ីនមេបានផ្ដល់គេហទំព័រ ជំនួសឱ្យទិន្នន័យនិស្សិត។");
            A("Check the address (it must be the API endpoint, not a website page) and the access token.",
                "ពិនិត្យអាសយដ្ឋាន (វាត្រូវតែជា API endpoint មិនមែនទំព័រគេហទំព័រ) និង token ចូលប្រើ។");
            A("Page {0} is not JSON.", "ទំព័រ {0} មិនមែនជា JSON ទេ។");
            A("{0} did not answer within {1} seconds.", "{0} មិនបានឆ្លើយតបក្នុងរយៈពេល {1} វិនាទីទេ។");
            A("The server answered {0} {1}.", "ម៉ាស៊ីនមេបានឆ្លើយតប {0} {1}។");
            A("Check the access token.", "សូមពិនិត្យ token ចូលប្រើ។");
            A("Check the API address.", "សូមពិនិត្យអាសយដ្ឋាន API។");
            A("Too many requests — wait a minute and try again.", "សំណើច្រើនពេក — រង់ចាំមួយនាទី រួចព្យាយាមម្ដងទៀត។");
            A("The problem is on the server; try again later or contact its administrator.",
                "បញ្ហានៅលើម៉ាស៊ីនមេ សូមព្យាយាមម្ដងទៀតពេលក្រោយ ឬទាក់ទងអ្នកគ្រប់គ្រងរបស់វា។");
            A("Server message:", "សារពីម៉ាស៊ីនមេ៖");
            A("The API response is not valid JSON: {0}", "ការឆ្លើយតបរបស់ API មិនមែនជា JSON ត្រឹមត្រូវទេ៖ {0}");
            A("The API response contains no student list.", "ការឆ្លើយតបរបស់ API មិនមានបញ្ជីនិស្សិតទេ។");
            A("Expected a JSON array, or an object with a \"data\", \"students\", \"items\", \"results\" or \"records\" array.",
                "រំពឹងថាជា JSON array ឬ object ដែលមាន array \"data\", \"students\", \"items\", \"results\" ឬ \"records\"។");
            A("Every entry in the student list must be a JSON object { … }.", "ធាតុនីមួយៗក្នុងបញ្ជីនិស្សិតត្រូវតែជា JSON object { … }។");
            A("The API's next-page link is not a valid address: {0}", "តំណទំព័របន្ទាប់របស់ API មិនមែនជាអាសយដ្ឋានត្រឹមត្រូវទេ៖ {0}");
            A("The API's next-page link points to another server ({0}).", "តំណទំព័របន្ទាប់របស់ API ចង្អុលទៅម៉ាស៊ីនមេផ្សេង ({0})។");
            A("It was not followed, so the access token is not sent there.", "វាមិនត្រូវបានធ្វើតាមទេ ដូច្នេះ token ចូលប្រើមិនត្រូវបានផ្ញើទៅទីនោះ។");
            A("The API's students have no StudentId field (student_id and studentId work too).",
                "និស្សិតរបស់ API គ្មានវាល StudentId ទេ (student_id និង studentId ក៏ប្រើបានដែរ)។");
            A("Fields found: {0}", "វាលដែលរកឃើញ៖ {0}");
            A("Enter the SQL Server name, e.g. localhost\\SQLEXPRESS or 192.168.1.10,1433",
                "បញ្ចូលឈ្មោះ SQL Server ឧ. localhost\\SQLEXPRESS ឬ 192.168.1.10,1433");
            A("Enter the SQL Server login name.", "បញ្ចូលឈ្មោះ login របស់ SQL Server។");
            A("The SQL result has no StudentId column (student_id and StudentID work too).",
                "លទ្ធផល SQL គ្មានជួរឈរ StudentId ទេ (student_id និង StudentID ក៏ប្រើបានដែរ)។");
            A("Columns found: {0}", "ជួរឈរដែលរកឃើញ៖ {0}");
            A("Rename it in the query, e.g.  SELECT code AS StudentId, …", "ប្ដូរឈ្មោះវាក្នុងសំណួរ ឧ.  SELECT code AS StudentId, …");
            A("Enter a table name (e.g. dbo.Students) or a SELECT query.", "បញ្ចូលឈ្មោះតារាង (ឧ. dbo.Students) ឬសំណួរ SELECT។");
            A("Only one SELECT query is allowed — the app never changes the database.",
                "អនុញ្ញាតតែសំណួរ SELECT មួយប៉ុណ្ណោះ — កម្មវិធីមិនដែលផ្លាស់ប្ដូរមូលដ្ឋានទិន្នន័យទេ។");
            A("The query failed: {0}", "សំណួរបរាជ័យ៖ {0}");
            A("Login failed. Check the user name and password, and that the server allows SQL Server authentication.",
                "ការចូលបរាជ័យ។ ពិនិត្យឈ្មោះអ្នកប្រើ និងពាក្យសម្ងាត់ ហើយថាម៉ាស៊ីនមេអនុញ្ញាត SQL Server authentication។");
            A("The database cannot be opened. Check the database name and that this login may use it.",
                "មិនអាចបើកមូលដ្ឋានទិន្នន័យបានទេ។ ពិនិត្យឈ្មោះមូលដ្ឋានទិន្នន័យ និងថា login នេះអាចប្រើវាបាន។");
            A("The server did not answer within {0} seconds.", "ម៉ាស៊ីនមេមិនបានឆ្លើយតបក្នុងរយៈពេល {0} វិនាទីទេ។");
            A("The SQL Server could not be found.", "រកមិនឃើញ SQL Server ទេ។");
            A("Check the server name (e.g. PCNAME\\SQLEXPRESS or 192.168.1.10,1433), that SQL Server allows remote connections over TCP/IP, and that the firewall lets port 1433 through.",
                "ពិនិត្យឈ្មោះម៉ាស៊ីនមេ (ឧ. PCNAME\\SQLEXPRESS ឬ 192.168.1.10,1433) ថា SQL Server អនុញ្ញាតការតភ្ជាប់ពីចម្ងាយតាម TCP/IP និងថា firewall អនុញ្ញាតច្រក 1433។");
            A("The server's certificate is not trusted. Tick “Trust server certificate” or turn off encryption.",
                "វិញ្ញាបនបត្ររបស់ម៉ាស៊ីនមេមិនត្រូវបានទុកចិត្ត។ ធីក “ទុកចិត្តវិញ្ញាបនបត្រម៉ាស៊ីនមេ” ឬបិទការអ៊ិនគ្រីប។");
            A("Cannot connect to SQL Server: {0}", "មិនអាចតភ្ជាប់ SQL Server បានទេ៖ {0}");

            // ---- RFID file and print history ----
            A("The student has no StudentId.", "និស្សិតគ្មាន StudentId ទេ។");
            A("Not a valid RFID card number: {0}", "មិនមែនជាលេខកាត RFID ត្រឹមត្រូវទេ៖ {0}");
            A("The folder of the RFID file cannot be created:", "មិនអាចបង្កើតថតនៃឯកសារ RFID បានទេ៖");
            A("The RFID file is open in another program (Excel?), so it cannot be changed.",
                "ឯកសារ RFID កំពុងបើកក្នុងកម្មវិធីផ្សេង (Excel?) ដូច្នេះមិនអាចផ្លាស់ប្ដូរវាបានទេ។");
            A("Close the file, then try again.", "សូមបិទឯកសារ រួចព្យាយាមម្ដងទៀត។");
            A("Windows does not allow this app to write the RFID file:", "Windows មិនអនុញ្ញាតឱ្យកម្មវិធីនេះសរសេរឯកសារ RFID ទេ៖");
            A("Choose another location with 'Change…' on the RFID tab, or check that the file is not read-only.",
                "ជ្រើសទីតាំងផ្សេងដោយ 'ផ្លាស់ប្ដូរ…' នៅផ្ទាំង RFID ឬពិនិត្យថាឯកសារមិនមែនសម្រាប់អានតែប៉ុណ្ណោះ។");
            A("The folder of the RFID file was not found:", "រកមិនឃើញថតនៃឯកសារ RFID ទេ៖");
            A("If it is on a network drive, check that it is connected.", "ប្រសិនបើវានៅលើដ្រាយបណ្ដាញ សូមពិនិត្យថាវាបានភ្ជាប់។");
            A("This file is not an RFID card list (it needs the columns StudentId and RfidUid):",
                "ឯកសារនេះមិនមែនជាបញ្ជីកាត RFID ទេ (វាត្រូវការជួរឈរ StudentId និង RfidUid)៖");
            A("The print history file has no header row:", "ឯកសារប្រវត្តិបោះពុម្ពគ្មានជួរក្បាលទេ៖");

            // ---- Google Drive / OneDrive photos ----
            A("Paste the link of the Google Drive folder with the photos.", "សូមបិទភ្ជាប់តំណថត Google Drive ដែលមានរូបថត។");
            A("This is not a Google Drive folder link.", "នេះមិនមែនជាតំណថត Google Drive ទេ។");
            A("In Google Drive right-click the folder → Share → Copy link, e.g.", "ក្នុង Google Drive ចុចកណ្ដុរស្ដាំលើថត → Share → Copy link ឧ.");
            A("The folder holds more than {0} images. Share the folder with the photos only.",
                "ថតមានរូបភាពលើសពី {0}។ សូមចែករំលែកតែថតដែលមានរូបថតប៉ុណ្ណោះ។");
            A("The folder holds more than {0} pictures. Share the folder with the photos only.",
                "ថតមានរូបភាពលើសពី {0}។ សូមចែករំលែកតែថតដែលមានរូបថតប៉ុណ្ណោះ។");
            A("The folder holds more than {0} pictures. Choose the folder with the photos only.",
                "ថតមានរូបភាពលើសពី {0}។ សូមជ្រើសតែថតដែលមានរូបថតប៉ុណ្ណោះ។");
            A("The download was incomplete. Try again.", "ការទាញយកមិនពេញលេញ។ សូមព្យាយាមម្ដងទៀត។");
            A("Not a readable JPG/PNG picture.", "មិនមែនជារូបភាព JPG/PNG ដែលអាចអានបានទេ។");
            A("Google Drive did not answer within {0} seconds.", "Google Drive មិនបានឆ្លើយតបក្នុងរយៈពេល {0} វិនាទីទេ។");
            A("The API key is not valid. Copy it again from Google Cloud Console → APIs & Services → Credentials.",
                "API key មិនត្រឹមត្រូវទេ។ ចម្លងវាម្ដងទៀតពី Google Cloud Console → APIs & Services → Credentials។");
            A("The Google Drive API is not turned on for this API key.", "Google Drive API មិនទាន់បើកសម្រាប់ API key នេះទេ។");
            A("In Google Cloud Console open APIs & Services → Library → Google Drive API → Enable, wait a minute, then try again.",
                "ក្នុង Google Cloud Console បើក APIs & Services → Library → Google Drive API → Enable រង់ចាំមួយនាទី រួចព្យាយាមម្ដងទៀត។");
            A("This API key is restricted and may not call the Google Drive API. Allow the Drive API in the key's restrictions.",
                "API key នេះត្រូវបានកំណត់ ហើយមិនអាចហៅ Google Drive API បានទេ។ សូមអនុញ្ញាត Drive API ក្នុងការកំណត់របស់ key។");
            A("Google Drive could not find the folder or the photo.", "Google Drive រកមិនឃើញថត ឬរូបថតទេ។");
            A("Check the link, and share the folder as “Anyone with the link” (Viewer).", "ពិនិត្យតំណ ហើយចែករំលែកថតជា “Anyone with the link” (Viewer)។");
            A("Google Drive is limiting requests. Wait a minute and try again.", "Google Drive កំពុងកំណត់សំណើ។ រង់ចាំមួយនាទី រួចព្យាយាមម្ដងទៀត។");
            A("Google Drive refused access. Share the folder as “Anyone with the link” (Viewer).",
                "Google Drive បានបដិសេធការចូលប្រើ។ សូមចែករំលែកថតជា “Anyone with the link” (Viewer)។");
            A("Google says: {0}", "Google និយាយថា៖ {0}");
            A("Google Drive answered {0}.", "Google Drive បានឆ្លើយតប {0}។");
            A("Google Drive answered {0}. {1}", "Google Drive បានឆ្លើយតប {0}។ {1}");
            A("Enter a Google API key. Click “Get a key” to create one (free) in Google Cloud Console.",
                "បញ្ចូល Google API key។ ចុច “យក Key” ដើម្បីបង្កើតមួយ (ឥតគិតថ្លៃ) ក្នុង Google Cloud Console។");
            A("Google Drive returned an unexpected answer.", "Google Drive បានផ្ដល់ចម្លើយដែលមិនរំពឹងទុក។");
            A("The sign-in code expired. Click Sign In… again.", "លេខកូដចូលគណនីបានផុតកំណត់។ ចុច ចូលគណនី… ម្ដងទៀត។");
            A("Sign in to OneDrive first: click Sign In… in OneDrive Settings.", "សូមចូល OneDrive ជាមុនសិន៖ ចុច ចូលគណនី… នៅ ការកំណត់ OneDrive។");
            A("The OneDrive sign-in has expired. Click Sign In… in OneDrive Settings again.",
                "ការចូល OneDrive បានផុតកំណត់។ ចុច ចូលគណនី… នៅ ការកំណត់ OneDrive ម្ដងទៀត។");
            A("This is not a OneDrive folder link.", "នេះមិនមែនជាតំណថត OneDrive ទេ។");
            A("In OneDrive right-click the folder → Share → Copy link, e.g. https://1drv.ms/f/… or https://…sharepoint.com/…",
                "ក្នុង OneDrive ចុចកណ្ដុរស្ដាំលើថត → Share → Copy link ឧ. https://1drv.ms/f/… ឬ https://…sharepoint.com/…");
            A("Enter the App ID (Application client ID) of your app registration. Click “Register App…”.",
                "បញ្ចូល App ID (Application client ID) នៃការចុះឈ្មោះកម្មវិធីរបស់អ្នក។ ចុច “ចុះឈ្មោះកម្មវិធី…”។");
            A("The App ID looks like 12345678-abcd-…: copy the “Application (client) ID” from the app registration.",
                "App ID មានរាងដូច 12345678-abcd-…៖ ចម្លង “Application (client) ID” ពីការចុះឈ្មោះកម្មវិធី។");
            A("This link opens a file, not a folder. Share the folder that holds the photos.",
                "តំណនេះបើកឯកសារ មិនមែនថតទេ។ សូមចែករំលែកថតដែលមានរូបថត។");
            A("OneDrive answered {0} while downloading the photo.", "OneDrive បានឆ្លើយតប {0} ពេលកំពុងទាញយករូបថត។");
            A("Microsoft sign-in answered {0}.", "ការចូលគណនី Microsoft បានឆ្លើយតប {0}។");
            A("OneDrive did not answer within {0} seconds.", "OneDrive មិនបានឆ្លើយតបក្នុងរយៈពេល {0} វិនាទីទេ។");
            A("Microsoft does not know this App ID. Copy the “Application (client) ID” from the app registration again.",
                "Microsoft មិនស្គាល់ App ID នេះទេ។ ចម្លង “Application (client) ID” ពីការចុះឈ្មោះកម្មវិធីម្ដងទៀត។");
            A("In the app registration open Authentication → “Allow public client flows” → Yes → Save, then sign in again.",
                "ក្នុងការចុះឈ្មោះកម្មវិធី បើក Authentication → “Allow public client flows” → Yes → Save រួចចូលគណនីម្ដងទៀត។");
            A("The app registration does not allow this kind of account. In Authentication set Supported account types to “Accounts in any organizational directory and personal Microsoft accounts”.",
                "ការចុះឈ្មោះកម្មវិធីមិនអនុញ្ញាតប្រភេទគណនីនេះទេ។ ក្នុង Authentication កំណត់ Supported account types ទៅ “Accounts in any organizational directory and personal Microsoft accounts”។");
            A("Your school's Microsoft 365 needs an administrator to approve the app (Files.Read.All). Ask the IT admin to grant consent.",
                "Microsoft 365 របស់សាលាត្រូវការអ្នកគ្រប់គ្រងអនុម័តកម្មវិធី (Files.Read.All)។ សូមស្នើអ្នកគ្រប់គ្រង IT ឱ្យផ្ដល់ការយល់ព្រម។");
            A("Sign-in was cancelled in the browser.", "ការចូលគណនីត្រូវបានបោះបង់ក្នុងកម្មវិធីរុករក។");
            A("OneDrive could not find the folder or the photo. Check the link, and that it is shared with this account or as “Anyone with the link”.",
                "OneDrive រកមិនឃើញថត ឬរូបថតទេ។ ពិនិត្យតំណ និងថាវាត្រូវបានចែករំលែកជាមួយគណនីនេះ ឬជា “Anyone with the link”។");
            A("OneDrive refused access. Share the folder with the signed-in account, or create an “Anyone with the link” link.",
                "OneDrive បានបដិសេធការចូលប្រើ។ សូមចែករំលែកថតជាមួយគណនីដែលបានចូល ឬបង្កើតតំណ “Anyone with the link”។");
            A("OneDrive refused access. Share the folder with the signed-in account, or create an “Anyone with the link” link.  OneDrive says: {0}",
                "OneDrive បានបដិសេធការចូលប្រើ។ សូមចែករំលែកថតជាមួយគណនីដែលបានចូល ឬបង្កើតតំណ “Anyone with the link”។  OneDrive និយាយថា៖ {0}");
            A("OneDrive did not accept the sign-in. Click Sign In… in OneDrive Settings again.",
                "OneDrive មិនទទួលយកការចូលគណនីទេ។ ចុច ចូលគណនី… នៅ ការកំណត់ OneDrive ម្ដងទៀត។");
            A("OneDrive is limiting requests. Wait a minute and try again.", "OneDrive កំពុងកំណត់សំណើ។ រង់ចាំមួយនាទី រួចព្យាយាមម្ដងទៀត។");
            A("OneDrive returned an unexpected answer.", "OneDrive បានផ្ដល់ចម្លើយដែលមិនរំពឹងទុក។");
            A("OneDrive answered {0}.", "OneDrive បានឆ្លើយតប {0}។");
            A("OneDrive answered {0}. {1}", "OneDrive បានឆ្លើយតប {0}។ {1}");
            A("Choose the OneDrive folder with the photos: click Browse….", "ជ្រើសថត OneDrive ដែលមានរូបថត៖ ចុច រកមើល…។");
            A("This is not a valid folder path: {0}", "នេះមិនមែនជាផ្លូវថតត្រឹមត្រូវទេ៖ {0}");
            A("This is the Photo Folder itself. Choose the OneDrive folder the photos come from.",
                "នេះជាថតរូបថតផ្ទាល់។ សូមជ្រើសថត OneDrive ដែលរូបថតមកពី។");
            A("The file is no longer in the OneDrive folder. Scan again.", "ឯកសារលែងនៅក្នុងថត OneDrive ហើយ។ សូមស្កេនម្ដងទៀត។");
            A("OneDrive could not provide the file. Check that OneDrive is running, signed in and online. ({0})",
                "OneDrive មិនអាចផ្ដល់ឯកសារបានទេ។ ពិនិត្យថា OneDrive កំពុងដំណើរការ បានចូលគណនី និងនៅលើបណ្ដាញ។ ({0})");

            // ---- Updates ----
            A("The update source did not answer within {0} seconds: {1}", "ប្រភពកំណែថ្មីមិនបានឆ្លើយតបក្នុងរយៈពេល {0} វិនាទីទេ៖ {1}");
            A("No update address is set. Enter the folder or https:// address where new versions are published.",
                "មិនទាន់កំណត់អាសយដ្ឋានកំណែថ្មីទេ។ សូមបញ្ចូលថត ឬអាសយដ្ឋាន https:// ដែលកំណែថ្មីត្រូវបានផ្សព្វផ្សាយ។");
            A("This is neither an https:// address nor a folder path: {0}", "នេះមិនមែនជាអាសយដ្ឋាន https:// ឬផ្លូវថតទេ៖ {0}");
            A("Plain http:// addresses are not allowed for updates: the setup runs as administrator, so it must come over https:// or from a network folder.",
                "អាសយដ្ឋាន http:// ធម្មតាមិនត្រូវបានអនុញ្ញាតសម្រាប់កំណែថ្មីទេ៖ កម្មវិធីដំឡើងដំណើរការជាអ្នកគ្រប់គ្រង ដូច្នេះវាត្រូវតែមកតាម https:// ឬពីថតបណ្ដាញ។");
            A("Use an https:// address or a folder path for updates, not {0}", "ប្រើអាសយដ្ឋាន https:// ឬផ្លូវថតសម្រាប់កំណែថ្មី មិនមែន {0}");
            A("No published release was found on GitHub (404). The repository may be private, or it has no release yet:",
                "រកមិនឃើញកំណែដែលបានផ្សព្វផ្សាយនៅលើ GitHub ទេ (404)។ Repository ប្រហែលជាឯកជន ឬមិនទាន់មានកំណែ៖");
            A("No update information was found at this address (404):", "រកមិនឃើញព័ត៌មានកំណែថ្មីនៅអាសយដ្ឋាននេះទេ (404)៖");
            A("The update server answered {0} {1}:", "ម៉ាស៊ីនមេកំណែថ្មីបានឆ្លើយតប {0} {1}៖");
            A("Could not reach {0}", "មិនអាចទៅដល់ {0} បានទេ");
            A("There is no {0} in {1}.", "គ្មាន {0} នៅក្នុង {1} ទេ។");
            A("Copy the update.json and CardStudio-Setup.exe from dist\\ there.", "ចម្លង update.json និង CardStudio-Setup.exe ពី dist\\ ទៅទីនោះ។");
            A("The update folder cannot be reached: {0}", "មិនអាចទៅដល់ថតកំណែថ្មីបានទេ៖ {0}");
            A("Check the network connection and the address.", "សូមពិនិត្យការតភ្ជាប់បណ្ដាញ និងអាសយដ្ឋាន។");
            A("The update information is not valid JSON:", "ព័ត៌មានកំណែថ្មីមិនមែនជា JSON ត្រឹមត្រូវទេ៖");
            A("The update information is empty or not a JSON object:", "ព័ត៌មានកំណែថ្មីទទេ ឬមិនមែនជា JSON object ទេ៖");
            A("The latest GitHub release ({0}) has no setup .exe attached.", "កំណែ GitHub ចុងក្រោយ ({0}) មិនមានភ្ជាប់កម្មវិធីដំឡើង .exe ទេ។");
            A("The update information has no \"version\":", "ព័ត៌មានកំណែថ្មីគ្មាន \"version\" ទេ៖");
            A("\"{0}\" is not a version number (expected e.g. 1.0.3):", "\"{0}\" មិនមែនជាលេខកំណែទេ (រំពឹង ឧ. 1.0.3)៖");
            A("The update information has no \"url\" of the setup:", "ព័ត៌មានកំណែថ្មីគ្មាន \"url\" នៃកម្មវិធីដំឡើងទេ៖");
            A("\"sha256\" must be 64 hexadecimal characters:", "\"sha256\" ត្រូវតែមានតួអក្សរគោលដប់ប្រាំមួយ 64៖");
            A("The download did not finish within {0} minutes.", "ការទាញយកមិនបានបញ្ចប់ក្នុងរយៈពេល {0} នាទីទេ។");
            A("The setup could not be downloaded: the server answered {0} {1}.", "មិនអាចទាញយកកម្មវិធីដំឡើងបានទេ៖ ម៉ាស៊ីនមេបានឆ្លើយតប {0} {1}។");
            A("The setup named in update.json was not found:", "រកមិនឃើញកម្មវិធីដំឡើងដែលមានឈ្មោះក្នុង update.json ទេ៖");
            A("The downloaded file is not a Windows program (the server may have sent an error page). Nothing was installed.",
                "ឯកសារដែលបានទាញយកមិនមែនជាកម្មវិធី Windows ទេ (ម៉ាស៊ីនមេប្រហែលជាបានផ្ញើទំព័រកំហុស)។ មិនបានដំឡើងអ្វីទេ។");
            A("The downloaded setup does not match the published SHA-256 checksum: it is damaged or not the published file. It was deleted and nothing was installed.",
                "កម្មវិធីដំឡើងដែលបានទាញយកមិនត្រូវនឹង SHA-256 checksum ដែលបានផ្សព្វផ្សាយទេ៖ វាខូច ឬមិនមែនជាឯកសារដែលបានផ្សព្វផ្សាយ។ វាត្រូវបានលុប ហើយមិនបានដំឡើងអ្វីទេ។");
            A("Expected {0}", "រំពឹង {0}");
            A("Received {0}", "ទទួលបាន {0}");
        }
    }
}
