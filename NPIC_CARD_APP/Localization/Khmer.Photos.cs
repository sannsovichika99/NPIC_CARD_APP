namespace NPIC_CARD_APP
{
    // Photo Folder, Google Drive / OneDrive Photos, Edit Photo (Remove Background) and Folders pages.
    internal static partial class KhmerStrings
    {
        private static void Photos()
        {
            // ---- Photo Folder ----
            A("Photo Folder", "ថតរូបថត");
            A("Where the cards read student photos from, and how to fill it", "កន្លែងដែលកាតអានរូបថតនិស្សិត និងរបៀបបំពេញវា");
            A("Choose Photo Folder…", "ជ្រើសថតរូបថត…");
            A("Pick the folder with photos named {StudentId}.jpg / .png. The cards show the new photos straight away.",
                "ជ្រើសថតដែលមានរូបថតដាក់ឈ្មោះ {StudentId}.jpg / .png។ កាតបង្ហាញរូបថតថ្មីភ្លាមៗ។");
            A("Open Photo Folder", "បើកថតរូបថត");
            A("Open the folder in Windows Explorer to add, rename or delete photos. The selected student's photo is highlighted.",
                "បើកថតក្នុង Windows Explorer ដើម្បីបន្ថែម ប្ដូរឈ្មោះ ឬលុបរូបថត។ រូបថតនិស្សិតដែលបានជ្រើសត្រូវបានរំលេច។");
            A("Google Drive Photos…", "រូបថត Google Drive…");
            A("Download student photos from a shared Google Drive folder into the photo folder.",
                "ទាញយករូបថតនិស្សិតពីថត Google Drive ដែលបានចែករំលែក ចូលថតរូបថត។");
            A("OneDrive Photos…", "រូបថត OneDrive…");
            A("Download student photos from a OneDrive folder link (Microsoft sign-in) or a synced OneDrive folder.",
                "ទាញយករូបថតនិស្សិតពីតំណថត OneDrive (ចូលគណនី Microsoft) ឬថត OneDrive ដែលបានធ្វើសមកាលកម្ម។");
            A("No photo folder is set yet. Choose one so the cards can show student photos.",
                "មិនទាន់កំណត់ថតរូបថតទេ។ សូមជ្រើសមួយ ដើម្បីឱ្យកាតបង្ហាញរូបថតនិស្សិត។");
            A("Current folder not found:", "រកមិនឃើញថតបច្ចុប្បន្ន៖");
            A("Choose the folder again.", "សូមជ្រើសថតម្ដងទៀត។");
            A("Current folder: {0}", "ថតបច្ចុប្បន្ន៖ {0}");
            A("Cannot read it: {0}", "មិនអាចអានវាបានទេ៖ {0}");
            A("{0} photo", "រូបថត {0}");
            A("{0} photos", "រូបថត {0}");
            A("  ·  every loaded student has a photo", "  ·  និស្សិតដែលបានផ្ទុកទាំងអស់មានរូបថត");
            A("  ·  {0} of {1} loaded students have no photo", "  ·  និស្សិត {0} ក្នុងចំណោម {1} ដែលបានផ្ទុក គ្មានរូបថត");

            // ---- Photo sync page (shared by Google Drive and OneDrive) ----
            A("Google Drive Photos", "រូបថត Google Drive");
            A("OneDrive Photos", "រូបថត OneDrive");
            A("Student Photos", "រូបថតនិស្សិត");
            A("Scan Folder (F5)", "ស្កេនថត (F5)");
            A("Save to", "រក្សាទុកទៅ");
            A("Replace photos that changed on {0}", "ជំនួសរូបថតដែលបានផ្លាស់ប្ដូរនៅ {0}");
            A("Unticked: a photo that differs on {0} keeps the copy in the Photo Folder. Photos edited with Remove Background are always kept.",
                "ដោះធីក៖ រូបថតដែលខុសគ្នានៅ {0} រក្សាច្បាប់ចម្លងក្នុងថតរូបថត។ រូបថតដែលកែដោយ លុបផ្ទៃខាងក្រោយ តែងតែត្រូវរក្សាទុក។");
            A("How to connect", "របៀបភ្ជាប់");
            A("Download", "ទាញយក");
            A("Connection", "ការតភ្ជាប់");
            A("File on {0}", "ឯកសារនៅ {0}");
            A("Result", "លទ្ធផល");
            A("Open Drive Settings", "បើកការកំណត់ Drive");
            A("Open OneDrive Settings", "បើកការកំណត់ OneDrive");
            A("Drive Settings", "ការកំណត់ Drive");
            A("OneDrive Settings", "ការកំណត់ OneDrive");
            A("Download Photos", "ទាញយករូបថត");
            A("Ctrl+Tab switches between the photo list and the {0} settings",
                "Ctrl+Tab ប្ដូររវាងបញ្ជីរូបថត និងការកំណត់ {0}");
            A("List the photos in the {0} folder and compare them with the Photo Folder. Nothing is written yet.",
                "បង្ហាញរូបថតក្នុងថត {0} ហើយប្រៀបធៀបវាជាមួយថតរូបថត។ មិនទាន់សរសេរអ្វីទេ។");
            A("Choose the Photo Folder the cards read photos from", "ជ្រើសថតរូបថតដែលកាតអានរូបថតពី");
            A("Ticked: a photo that differs from the one on {0} is replaced by the {1} copy.",
                "ធីក៖ រូបថតដែលខុសពីរូបនៅ {0} ត្រូវបានជំនួសដោយច្បាប់ចម្លងពី {1}។");
            A("Photos edited with Remove Background are always kept.", "រូបថតដែលកែដោយ លុបផ្ទៃខាងក្រោយ តែងតែត្រូវរក្សាទុក។");
            A("No student list loaded: file names are taken as the StudentId. Import the students first to match names like “NPIC24001 - Sok Dara.jpg”.",
                "មិនទាន់ផ្ទុកបញ្ជីនិស្សិត៖ ឈ្មោះឯកសារត្រូវបានយកជា StudentId។ នាំចូលនិស្សិតជាមុន ដើម្បីផ្គូផ្គងឈ្មោះដូចជា “NPIC24001 - Sok Dara.jpg”។");
            A("Reading the {0} folder…  (Stop cancels)", "កំពុងអានថត {0}…  (បញ្ឈប់ ដើម្បីបោះបង់)");
            A("Downloading 0 / {0} photo(s)…  (Stop cancels)", "កំពុងទាញយក 0 / {0} រូបថត…  (បញ្ឈប់ ដើម្បីបោះបង់)");
            A("Downloading {0} / {1} photo(s)…  (Stop cancels)", "កំពុងទាញយក {0} / {1} រូបថត…  (បញ្ឈប់ ដើម្បីបោះបង់)");
            A("{0}{1} photo(s) downloaded to {2}{3}", "{0}បានទាញយករូបថត {1} ទៅ {2}{3}");
            A("Stopped — ", "បានបញ្ឈប់ — ");
            A("  ·  {0} failed (see Result)", "  ·  {0} បរាជ័យ (មើល លទ្ធផល)");
            A("{0} new", "{0} ថ្មី");
            A("{0} changed", "{0} បានផ្លាស់ប្ដូរ");
            A("{0} downloaded", "{0} បានទាញយក");
            A("{0} not matched", "{0} មិនត្រូវគ្នា");
            A("{0} not JPG/PNG", "{0} មិនមែន JPG/PNG");
            A("{0} failed", "{0} បរាជ័យ");
            A("{0} up to date", "{0} ទាន់សម័យ");
            A("{0} edited here", "{0} បានកែនៅទីនេះ");
            A("{0} older copies", "{0} ច្បាប់ចម្លងចាស់");
            A("{0} to download", "{0} ត្រូវទាញយក");
            A("{0} need attention", "{0} ត្រូវការការយកចិត្តទុកដាក់");
            A("All up to date", "ទាន់សម័យទាំងអស់");
            A("Not scanned yet.", "មិនទាន់ស្កេនទេ។");
            A("Click Scan Folder (F5) to list the student photos on {0}.", "ចុច ស្កេនថត (F5) ដើម្បីបង្ហាញរូបថតនិស្សិតនៅ {0}។");
            A("New — will download", "ថ្មី — នឹងទាញយក");
            A("Changed — will replace", "បានផ្លាស់ប្ដូរ — នឹងជំនួស");
            A("Changed on {0} — kept", "បានផ្លាស់ប្ដូរនៅ {0} — រក្សាទុក");
            A("Up to date", "ទាន់សម័យ");
            A("Kept — edited in the app", "រក្សាទុក — បានកែក្នុងកម្មវិធី");
            A("No student with this ID", "គ្មាននិស្សិតមានអត្តលេខនេះ");
            A("Not JPG/PNG — skipped", "មិនមែន JPG/PNG — បានរំលង");
            A("Older copy — newer one used", "ច្បាប់ចម្លងចាស់ — បានប្រើច្បាប់ថ្មីជាង");
            A("✔ Downloaded", "✔ បានទាញយក");
            A("Failed: {0}", "បរាជ័យ៖ {0}");
            A("{0} picture(s) on {1}", "រូបភាព {0} នៅ {1}");
            A(" in {0} folders", " ក្នុងថត {0}");
            A("Scanned {0:HH:mm} — {1}", "បានស្កេន {0:HH:mm} — {1}");
            A("Photo Folder — the {0} photos are saved here as {{StudentId}}.jpg / .png",
                "ថតរូបថត — រូបថតពី {0} ត្រូវបានរក្សាទុកនៅទីនេះជា {{StudentId}}.jpg / .png");
            A("No Photo Folder yet — click Change… (or Download asks for one)", "មិនទាន់មានថតរូបថត — ចុច ផ្លាស់ប្ដូរ… (ឬ ទាញយក នឹងសួររក)");
            A("Saving to  {0}", "រក្សាទុកទៅ  {0}");
            A("No Photo Folder yet — set it in {0}", "មិនទាន់មានថតរូបថត — កំណត់វានៅ {0}");
            A("Download {0} Photo(s)", "ទាញយករូបថត {0}");
            A("Could not open the browser: {0}", "មិនអាចបើកកម្មវិធីរុករកបានទេ៖ {0}");
            A("Could not open the folder: {0}", "មិនអាចបើកថតបានទេ៖ {0}");

            // ---- Google Drive ----
            A("Paste the folder link: https://drive.google.com/drive/folders/…", "បិទភ្ជាប់តំណថត៖ https://drive.google.com/drive/folders/…");
            A("Google API key, e.g. AIzaSy…", "Google API key ឧ. AIzaSy…");
            A("Get a Key…", "យក Key…");
            A("Drive", "Drive");
            A("Download student photos from a shared Google Drive folder", "ទាញយករូបថតនិស្សិតពីថត Google Drive ដែលបានចែករំលែក");
            A("Shared folder", "ថតដែលបានចែករំលែក");
            A("1.  In Google Drive right-click the folder → Share → General access → “Anyone with the link” (Viewer) → Copy link.",
                "1.  ក្នុង Google Drive ចុចកណ្ដុរស្ដាំលើថត → Share → General access → “Anyone with the link” (Viewer) → Copy link។");
            A("2.  Paste the link above. Photos in sub-folders (e.g. Google Form uploads) are found too.",
                "2.  បិទភ្ជាប់តំណនៅខាងលើ។ រូបថតក្នុងថតរង (ឧ. ការផ្ទុកឡើងពី Google Form) ក៏ត្រូវបានរកឃើញដែរ។");
            A("3.  Paste a Google API key with the Google Drive API turned on — click “Get a Key…” (free, once per school).",
                "3.  បិទភ្ជាប់ Google API key ដែលបានបើក Google Drive API — ចុច “យក Key…” (ឥតគិតថ្លៃ ម្ដងសម្រាប់សាលានីមួយៗ)។");
            A("4.  Click Scan Folder (F5), check the Student Photos tab, then Download.",
                "4.  ចុច ស្កេនថត (F5) ពិនិត្យផ្ទាំង រូបថតនិស្សិត រួច ទាញយក។");
            A("Files named NPIC24001.jpg or “NPIC24001 - Sok Dara.jpg” go to student NPIC24001.",
                "ឯកសារឈ្មោះ NPIC24001.jpg ឬ “NPIC24001 - Sok Dara.jpg” ទៅនិស្សិត NPIC24001។");
            A("Only new photos are downloaded; photos edited with Remove Background are never replaced.",
                "មានតែរូបថតថ្មីប៉ុណ្ណោះដែលត្រូវទាញយក រូបថតដែលកែដោយ លុបផ្ទៃខាងក្រោយ មិនដែលត្រូវជំនួសទេ។");
            A("Google Drive is not connected yet.", "Google Drive មិនទាន់ភ្ជាប់ទេ។");
            A("Add the shared folder link and an API key in Drive Settings.", "បន្ថែមតំណថតដែលបានចែករំលែក និង API key នៅ ការកំណត់ Drive។");
            A("The Google Drive folder has no pictures.", "ថត Google Drive គ្មានរូបភាពទេ។");
            A("Check the link, or upload the photos to it.", "ពិនិត្យតំណ ឬផ្ទុករូបថតឡើងទៅវា។");
            A("Folder link", "តំណថត");
            A("API key", "API key");
            A("Google Cloud Console → Create credentials → API key, then APIs & Services → Library → Google Drive API → Enable. " +
              "The key is saved encrypted for this Windows user.",
                "Google Cloud Console → Create credentials → API key រួច APIs & Services → Library → Google Drive API → Enable។ " +
                "Key ត្រូវបានរក្សាទុកដោយអ៊ិនគ្រីបសម្រាប់អ្នកប្រើ Windows នេះ។");
            A("Enter a Google API key. Click “Get a Key…” to create one (free).", "បញ្ចូល Google API key។ ចុច “យក Key…” ដើម្បីបង្កើតមួយ (ឥតគិតថ្លៃ)។");

            // ---- OneDrive ----
            A("Paste the OneDrive folder link: https://1drv.ms/f/…  (or Browse… for a synced folder)",
                "បិទភ្ជាប់តំណថត OneDrive៖ https://1drv.ms/f/…  (ឬ រកមើល… សម្រាប់ថតដែលបានធ្វើសមកាលកម្ម)");
            A("Application (client) ID, e.g. 1a2b3c4d-…  — click Register App…", "Application (client) ID ឧ. 1a2b3c4d-…  — ចុច ចុះឈ្មោះកម្មវិធី…");
            A("Register App…", "ចុះឈ្មោះកម្មវិធី…");
            A("Sign In…", "ចូលគណនី…");
            A("Sign Out", "ចាកចេញ");
            A("Download student photos from a OneDrive folder", "ទាញយករូបថតនិស្សិតពីថត OneDrive");
            A("1.  Once per school: click “Register App…” → New registration → any name (e.g. Card Studio) → Supported account types: " +
              "“Accounts in any organizational directory and personal Microsoft accounts” → Register.",
                "1.  ម្ដងសម្រាប់សាលានីមួយៗ៖ ចុច “ចុះឈ្មោះកម្មវិធី…” → New registration → ឈ្មោះណាមួយ (ឧ. Card Studio) → Supported account types៖ " +
                "“Accounts in any organizational directory and personal Microsoft accounts” → Register។");
            A("Copy the “Application (client) ID” into App ID, then open Authentication → “Allow public client flows” → Yes → Save.",
                "ចម្លង “Application (client) ID” ទៅក្នុង App ID រួចបើក Authentication → “Allow public client flows” → Yes → Save។");
            A("2.  In OneDrive right-click the photo folder → Share → Copy link, and paste it above (or type a path in your own OneDrive, e.g. /Photos/2025).",
                "2.  ក្នុង OneDrive ចុចកណ្ដុរស្ដាំលើថតរូបថត → Share → Copy link ហើយបិទភ្ជាប់នៅខាងលើ (ឬវាយផ្លូវក្នុង OneDrive របស់អ្នក ឧ. /Photos/2025)។");
            A("3.  Click Sign In…, enter the code shown in the browser that opens, and sign in with your Microsoft account.",
                "3.  ចុច ចូលគណនី… បញ្ចូលលេខកូដដែលបង្ហាញក្នុងកម្មវិធីរុករកដែលបើកឡើង ហើយចូលដោយគណនី Microsoft របស់អ្នក។");
            A("Files named NPIC24001.jpg or “NPIC24001 - Sok Dara.jpg” go to student NPIC24001. Sub-folders are searched too.",
                "ឯកសារឈ្មោះ NPIC24001.jpg ឬ “NPIC24001 - Sok Dara.jpg” ទៅនិស្សិត NPIC24001។ ថតរងក៏ត្រូវបានស្វែងរកដែរ។");
            A("Only new photos are downloaded; photos edited with Remove Background are never replaced.",
                "មានតែរូបថតថ្មីប៉ុណ្ណោះដែលត្រូវទាញយក រូបថតដែលកែដោយ លុបផ្ទៃខាងក្រោយ មិនដែលត្រូវជំនួសទេ។");
            A("No internet app registration? Browse… to a OneDrive folder synced to this PC instead — no sign-in needed.",
                "គ្មានការចុះឈ្មោះកម្មវិធីលើអ៊ីនធឺណិត? ប្រើ រកមើល… ទៅថត OneDrive ដែលបានធ្វើសមកាលកម្មលើកុំព្យូទ័រនេះជំនួសវិញ — មិនចាំបាច់ចូលគណនី។");
            A("OneDrive is not connected yet.", "OneDrive មិនទាន់ភ្ជាប់ទេ។");
            A("Add the folder link, the App ID and sign in, in OneDrive Settings.", "បន្ថែមតំណថត App ID ហើយចូលគណនី នៅ ការកំណត់ OneDrive។");
            A("The OneDrive folder has no pictures.", "ថត OneDrive គ្មានរូបភាពទេ។");
            A("Check the folder, or upload the photos to it.", "ពិនិត្យថត ឬផ្ទុករូបថតឡើងទៅវា។");
            A("App ID", "App ID");
            A("Account", "គណនី");
            A("Choose a OneDrive folder synced to this PC instead of a link (no sign-in needed)",
                "ជ្រើសថត OneDrive ដែលបានធ្វើសមកាលកម្មលើកុំព្យូទ័រនេះ ជំនួសតំណ (មិនចាំបាច់ចូលគណនី)");
            A("Open the folder in the browser (link) or in Windows Explorer (synced folder)",
                "បើកថតក្នុងកម្មវិធីរុករក (តំណ) ឬក្នុង Windows Explorer (ថតដែលបានធ្វើសមកាលកម្ម)");
            A("Microsoft Entra → App registrations → New registration. Free; done once per school.",
                "Microsoft Entra → App registrations → New registration។ ឥតគិតថ្លៃ ធ្វើម្ដងសម្រាប់សាលានីមួយៗ។");
            A("Sign in with the Microsoft account that can open the folder. The app never sees the password; the sign-in is saved encrypted for this Windows user.",
                "ចូលដោយគណនី Microsoft ដែលអាចបើកថតបាន។ កម្មវិធីមិនដែលឃើញពាក្យសម្ងាត់ទេ ការចូលគណនីត្រូវបានរក្សាទុកដោយអ៊ិនគ្រីបសម្រាប់អ្នកប្រើ Windows នេះ។");
            A("Finish signing in first: enter the code in the browser.", "សូមបញ្ចប់ការចូលគណនីជាមុនសិន៖ បញ្ចូលលេខកូដក្នុងកម្មវិធីរុករក។");
            A("Sign in to OneDrive: click Sign In… and enter the code in the browser.", "ចូល OneDrive៖ ចុច ចូលគណនី… ហើយបញ្ចូលលេខកូដក្នុងកម្មវិធីរុករក។");
            A("Contacting Microsoft…", "កំពុងទាក់ទង Microsoft…");
            A("Enter the code  {0}  at {1}  (copied — paste it in the browser).  Waiting…",
                "បញ្ចូលលេខកូដ  {0}  នៅ {1}  (បានចម្លង — បិទភ្ជាប់វាក្នុងកម្មវិធីរុករក)។  កំពុងរង់ចាំ…");
            A("Enter the code  {0}  at {1}.  Waiting…", "បញ្ចូលលេខកូដ  {0}  នៅ {1}។  កំពុងរង់ចាំ…");
            A("Microsoft account", "គណនី Microsoft");
            A("Choose a OneDrive folder synced to this PC that holds the student photos",
                "ជ្រើសថត OneDrive ដែលបានធ្វើសមកាលកម្មលើកុំព្យូទ័រនេះ ដែលមានរូបថតនិស្សិត");
            A("Folder on this PC — no sign-in needed.  ✔ Synced by OneDrive — {0}",
                "ថតលើកុំព្យូទ័រនេះ — មិនចាំបាច់ចូលគណនី។  ✔ បានធ្វើសមកាលកម្មដោយ OneDrive — {0}");
            A("Folder on this PC — no sign-in needed. It is not inside OneDrive, so it is read as a normal folder.",
                "ថតលើកុំព្យូទ័រនេះ — មិនចាំបាច់ចូលគណនី។ វាមិននៅក្នុង OneDrive ទេ ដូច្នេះវាត្រូវបានអានជាថតធម្មតា។");
            A("✔  Signed in as {0}", "✔  បានចូលជា {0}");
            A("a Microsoft account", "គណនី Microsoft មួយ");
            A("The App ID changed — click Sign In… again.", "App ID បានផ្លាស់ប្ដូរ — ចុច ចូលគណនី… ម្ដងទៀត។");
            A("Not signed in. Enter the App ID first, then click Sign In….", "មិនទាន់ចូលគណនី។ បញ្ចូល App ID ជាមុនសិន រួចចុច ចូលគណនី…។");
            A("Not signed in. Click Sign In… and enter the code in the browser.", "មិនទាន់ចូលគណនី។ ចុច ចូលគណនី… ហើយបញ្ចូលលេខកូដក្នុងកម្មវិធីរុករក។");

            // ---- Edit Photo (Remove Background) ----
            A("No photo found for {0} in {1}", "រកមិនឃើញរូបថតសម្រាប់ {0} ក្នុង {1}");
            A("Edit Photo", "កែរូបថត");
            A("Crop and clean the background of student {0}", "កាត់ និងសម្អាតផ្ទៃខាងក្រោយរបស់និស្សិត {0}");
            A("Original — drag to crop", "ដើម — អូសដើម្បីកាត់");
            A("White", "ស");
            A("Blue", "ខៀវ");
            A("Transparent", "ថ្លា");
            A("Keep", "រក្សាដដែល");
            A("Crop only: leave the background as it is", "កាត់តែប៉ុណ្ណោះ៖ ទុកផ្ទៃខាងក្រោយដដែល");
            A("Card photo shape", "រាងរូបថតលើកាត");
            A("Keep the frame at {0} x {1} mm, the size of the photo box on the card",
                "រក្សាស៊ុមនៅ {0} x {1} mm ដែលជាទំហំប្រអប់រូបថតលើកាត");
            A("Reset Crop", "កំណត់ការកាត់ឡើងវិញ");
            A("Crop", "កាត់");
            A("Tolerance", "កម្រិតអត់ធ្មត់");
            A("New background", "ផ្ទៃខាងក្រោយថ្មី");
            A("Drag inside the frame to move it, drag a corner to resize, drag outside to draw a new one (arrow keys nudge). " +
              "Raise the tolerance if background is left over; lower it if parts of the face, hair or clothes disappear.",
                "អូសក្នុងស៊ុមដើម្បីផ្លាស់ទី អូសជ្រុងដើម្បីប្ដូរទំហំ អូសខាងក្រៅដើម្បីគូរស៊ុមថ្មី (គ្រាប់ចុចព្រួញរំកិលបន្តិច)។ " +
                "បង្កើនកម្រិតអត់ធ្មត់ ប្រសិនបើផ្ទៃខាងក្រោយនៅសល់ បន្ថយវា ប្រសិនបើផ្នែកមុខ សក់ ឬសម្លៀកបំពាក់បាត់។");
            A("Save Photo", "រក្សាទុករូបថត");
            A("Restore Original", "ស្ដាររូបដើម");
            A("Could not process the photo:", "មិនអាចដំណើរការរូបថតបានទេ៖");
            A("Could not save the photo:", "មិនអាចរក្សាទុករូបថតបានទេ៖");
            A("If the photo is open in another program, close it and try again.", "ប្រសិនបើរូបថតកំពុងបើកក្នុងកម្មវិធីផ្សេង សូមបិទវា រួចព្យាយាមម្ដងទៀត។");
            A("Could not restore the original photo:", "មិនអាចស្ដាររូបថតដើមបានទេ៖");

            // ---- Folders ----
            A("Folders", "ថត");
            A("Every folder the app uses — open, change or create them", "រាល់ថតដែលកម្មវិធីប្រើ — បើក ប្ដូរ ឬបង្កើតវា");
            A("Search folders… (Ctrl+F)", "ស្វែងរកថត… (Ctrl+F)");
            A("Create", "បង្កើត");
            A("Copy Path", "ចម្លងផ្លូវ");
            A("Open the selected folder in Windows Explorer (Enter or double-click)", "បើកថតដែលបានជ្រើសក្នុង Windows Explorer (Enter ឬចុចពីរដង)");
            A("Choose another folder for the selected row", "ជ្រើសថតផ្សេងសម្រាប់ជួរដែលបានជ្រើស");
            A("Create the selected folder if it does not exist", "បង្កើតថតដែលបានជ្រើស ប្រសិនបើវាមិនទាន់មាន");
            A("Copy the folder path to the clipboard (Ctrl+C)", "ចម្លងផ្លូវថតទៅក្ដារតម្បៀតខ្ទាស់ (Ctrl+C)");
            A("Group", "ក្រុម");
            A("Contents", "មាតិកា");
            A("No folder matches the search.", "គ្មានថតត្រូវនឹងការស្វែងរកទេ។");
            A("(not set)", "(មិនទាន់កំណត់)");
            A("{0} folders", "ថត {0}");
            A("{0} folder", "ថត {0}");
            A("{0} files", "ឯកសារ {0}");
            A("{0} file", "ឯកសារ {0}");
            A("{0} files, {1} folders", "ឯកសារ {0}, ថត {1}");
            A("  ·  {0} need attention (orange)", "  ·  {0} ត្រូវការការយកចិត្តទុកដាក់ (ពណ៌ទឹកក្រូច)");
            A("  ·  all folders the app needs are there", "  ·  ថតទាំងអស់ដែលកម្មវិធីត្រូវការមានគ្រប់");
            A("Select a folder to see what it is used for.", "ជ្រើសថតមួយ ដើម្បីមើលថាវាប្រើសម្រាប់អ្វី។");
            A("This folder does not exist:", "ថតនេះមិនមានទេ៖");
            A("Use Create to make it.", "ប្រើ បង្កើត ដើម្បីបង្កើតវា។");
            A("Could not create the folder:", "មិនអាចបង្កើតថតបានទេ៖");
            A("Photo folder", "ថតរូបថត");
            A("Student photos named {StudentId}.jpg / .png — the cards read photos from here.",
                "រូបថតនិស្សិតដាក់ឈ្មោះ {StudentId}.jpg / .png — កាតអានរូបថតពីទីនេះ។");
            A("Original photos", "រូបថតដើម");
            A("Untouched photos kept by Remove Background… so Restore can bring them back. Created on first use.",
                "រូបថតមិនទាន់កែ ដែល លុបផ្ទៃខាងក្រោយ… រក្សាទុក ដើម្បីឱ្យ ស្ដារ អាចយកមកវិញ។ បង្កើតនៅពេលប្រើលើកដំបូង។");
            A("OneDrive photos", "រូបថត OneDrive");
            A("Online — sign-in needed", "អនឡាញ — ត្រូវចូលគណនី");
            A("OneDrive share link that OneDrive Photos… downloads student photos from.",
                "តំណចែករំលែក OneDrive ដែល រូបថត OneDrive… ទាញយករូបថតនិស្សិតពី។");
            A("OneDrive folder synced to this PC that OneDrive Photos… copies student photos from.",
                "ថត OneDrive ដែលបានធ្វើសមកាលកម្មលើកុំព្យូទ័រនេះ ដែល រូបថត OneDrive… ចម្លងរូបថតនិស្សិតពី។");
            A("Change it in Photo Folder… → OneDrive Photos… (Ctrl+Shift+O).", "ប្ដូរវានៅ ថតរូបថត… → រូបថត OneDrive… (Ctrl+Shift+O)។");
            A("App data", "ទិន្នន័យកម្មវិធី");
            A("Settings & history", "ការកំណត់ និងប្រវត្តិ");
            A("settings.xml, its backup, print-history.csv and (by default) rfid-cards.csv for this Windows user.",
                "settings.xml ច្បាប់បម្រុងទុករបស់វា print-history.csv និង (តាមលំនាំដើម) rfid-cards.csv សម្រាប់អ្នកប្រើ Windows នេះ។");
            A("QR logos", "ឡូហ្គោ QR");
            A("Copies of the logos chosen in Card Text… for the QR code, so cards still print if the original moves.",
                "ច្បាប់ចម្លងឡូហ្គោដែលបានជ្រើសក្នុង អត្ថបទលើកាត… សម្រាប់កូដ QR ដើម្បីឱ្យកាតនៅតែបោះពុម្ពបាន ទោះបីឯកសារដើមត្រូវផ្លាស់ទី។");
            A("Signatures", "ហត្ថលេខា");
            A("Copies of the signature images chosen in Card Text… → Front → Director signature.",
                "ច្បាប់ចម្លងរូបហត្ថលេខាដែលបានជ្រើសក្នុង អត្ថបទលើកាត… → ផ្នែកមុខ → ហត្ថលេខានាយក។");
            A("Program", "កម្មវិធី");
            A("App folder", "ថតកម្មវិធី");
            A("Where NPIC_CARD_APP.exe is installed.", "កន្លែងដែល NPIC_CARD_APP.exe ត្រូវបានដំឡើង។");
            A("Card images (Assets)", "រូបភាពកាត (Assets)");
            A("Logo, stamp, QR code and social icons printed on every card.", "ឡូហ្គោ ត្រា កូដ QR និងរូបតំណាងបណ្ដាញសង្គម ដែលបោះពុម្ពលើកាតនីមួយៗ។");
            A("Missing: {0}", "ខ្វះ៖ {0}");
            A("Files in use", "ឯកសារកំពុងប្រើ");
            A("Signature folder", "ថតហត្ថលេខា");
            A("Folder of the director's signature image. Change it in Card Text… → Front → Director signature. Default: {0}.",
                "ថតនៃរូបហត្ថលេខានាយក។ ប្ដូរវានៅ អត្ថបទលើកាត… → ផ្នែកមុខ → ហត្ថលេខានាយក។ លំនាំដើម៖ {0}។");
            A("Default {0}", "លំនាំដើម {0}");
            A("Default {0} — not found, no signature printed", "លំនាំដើម {0} — រកមិនឃើញ គ្មានហត្ថលេខាបោះពុម្ព");
            A("{0} — not found, no signature printed", "{0} — រកមិនឃើញ គ្មានហត្ថលេខាបោះពុម្ព");
            A("Last student file folder", "ថតឯកសារនិស្សិតចុងក្រោយ");
            A("Folder of the Excel / CSV file the students were last imported from.", "ថតនៃឯកសារ Excel / CSV ដែលនិស្សិតត្រូវបាននាំចូលចុងក្រោយ។");
            A("CSV folder", "ថត CSV");
            A("Folder listed by CSV Files… When it is not set, CSV Files… lists the last student file folder.",
                "ថតដែល ឯកសារ CSV… បង្ហាញ។ ពេលមិនទាន់កំណត់ ឯកសារ CSV… បង្ហាញថតឯកសារនិស្សិតចុងក្រោយ។");
            A("Change it in CSV Files… → Change Folder… (Ctrl+Shift+C).", "ប្ដូរវានៅ ឯកសារ CSV… → ប្ដូរថត… (Ctrl+Shift+C)។");
            A("RFID file folder", "ថតឯកសារ RFID");
            A("Folder of {0}, where the printed cards' RFID numbers are saved.", "ថតនៃ {0} ដែលលេខ RFID នៃកាតដែលបានបោះពុម្ពត្រូវបានរក្សាទុក។");
            A("Change it on the RFID tab → Change….", "ប្ដូរវានៅផ្ទាំង RFID → ផ្លាស់ប្ដូរ…។");
            A("✔ OK", "✔ ល្អ");
            A("Not found", "រកមិនឃើញ");
            A("Not set", "មិនទាន់កំណត់");
            A("Not created yet", "មិនទាន់បង្កើត");
            A("Online link", "តំណអនឡាញ");
            A("Cannot read: {0}", "មិនអាចអាន៖ {0}");
        }
    }
}
