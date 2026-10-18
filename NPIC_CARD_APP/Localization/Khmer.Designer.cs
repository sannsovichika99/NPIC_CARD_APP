namespace NPIC_CARD_APP
{
    // Card Designer: toolbar, menus, layer list, status line, element properties (Rendering\CustomCard.cs) and its small popups.
    internal static partial class KhmerStrings
    {
        private static void Designer()
        {
            // ---- Toolbar and panels ----
            A("Card Designer", "រចនាកាត");
            A("Design ▾", "ការរចនា ▾");
            A("Switch to another saved design, duplicate, rename or delete this one, start over, import or export",
                "ប្ដូរទៅការរចនាផ្សេងដែលបានរក្សាទុក ចម្លង ប្ដូរឈ្មោះ ឬលុបការរចនានេះ ចាប់ផ្ដើមឡើងវិញ នាំចូល ឬនាំចេញ");
            A("+ New Design", "+ ការរចនាថ្មី");
            A("Create another card design (your saved designs stay as they are)", "បង្កើតការរចនាកាតមួយទៀត (ការរចនាដែលបានរក្សាទុកនៅដដែល)");
            A("Edit the front (student side)", "កែផ្នែកមុខ (ផ្នែកនិស្សិត)");
            A("Edit the back (same for every student)", "កែផ្នែកក្រោយ (ដូចគ្នាសម្រាប់និស្សិតគ្រប់រូប)");
            A("Undo", "មិនធ្វើវិញ");
            A("Undo (Ctrl+Z)", "មិនធ្វើវិញ (Ctrl+Z)");
            A("Redo", "ធ្វើវិញ");
            A("Redo (Ctrl+Y)", "ធ្វើវិញ (Ctrl+Y)");
            A("Snap", "ស្អិត");
            A("Snap to a 0.5 mm grid and to the card's edges and centre lines", "ស្អិតទៅក្រឡា 0.5 mm និងទៅគែម និងបន្ទាត់កណ្ដាលរបស់កាត");
            A("Outlines", "គែមខាងក្រៅ");
            A("Show the outline of every element and the 1.5 mm safe margin (never printed)",
                "បង្ហាញគែមរបស់ធាតុនីមួយៗ និងរឹមសុវត្ថិភាព 1.5 mm (មិនបោះពុម្ព)");
            A("ADD", "បន្ថែម");
            A("Text", "អត្ថបទ");
            A("Fixed text, the same on every card", "អត្ថបទថេរ ដូចគ្នាលើកាតគ្រប់សន្លឹក");
            A("Field ▾", "វាល ▾");
            A("Student data (name, ID, date of birth…), a custom field from any column of your list, or Card Text (director, address…)",
                "ទិន្នន័យនិស្សិត (ឈ្មោះ អត្តលេខ ថ្ងៃខែឆ្នាំកំណើត…) វាលផ្ទាល់ខ្លួនពីជួរឈរណាមួយក្នុងបញ្ជី ឬអត្ថបទលើកាត (នាយក អាសយដ្ឋាន…)");
            A("Image ▾", "រូបភាព ▾");
            A("Upload your own picture, paste one (Ctrl+V) or use a logo, stamp, signature or icon. You can also drag picture files onto the card.",
                "ផ្ទុករូបភាពផ្ទាល់ខ្លួនឡើង បិទភ្ជាប់មួយ (Ctrl+V) ឬប្រើឡូហ្គោ ត្រា ហត្ថលេខា ឬរូបតំណាង។ អ្នកក៏អាចអូសឯកសាររូបភាពមកលើកាតបានដែរ។");
            A("The student's photo from the photo folder", "រូបថតនិស្សិតពីថតរូបថត");
            A("QR ▾", "QR ▾");
            A("Student QR, back QR (Card Text) or a QR with your own text", "QR និស្សិត QR ផ្នែកក្រោយ (អត្ថបទលើកាត) ឬ QR ដែលមានអត្ថបទផ្ទាល់ខ្លួន");
            A("Shape ▾", "រាង ▾");
            A("Rectangle, circle / ellipse, triangle, curve or line", "ចតុកោណ រង្វង់ / អេលីប ត្រីកោណ ខ្សែកោង ឬបន្ទាត់");
            A("LAYERS  ·  TOP FIRST", "ស្រទាប់  ·  ខាងលើមុន");
            A("Bring forward (Ctrl+])", "នាំមកមុខ (Ctrl+])");
            A("Send backward (Ctrl+[)", "បញ្ជូនទៅក្រោយ (Ctrl+[)");
            A("Duplicate (Ctrl+D)", "ចម្លងមួយទៀត (Ctrl+D)");
            A("Delete (Del)", "លុប (Del)");
            A("Save & Use", "រក្សាទុក និងប្រើ");
            A("Save the design and print with it from now on (Card Template → Custom)",
                "រក្សាទុកការរចនា ហើយបោះពុម្ពជាមួយវាចាប់ពីពេលនេះ (គំរូកាត → ផ្ទាល់ខ្លួន)");
            A("Save the design (Ctrl+S); printing keeps the current template", "រក្សាទុកការរចនា (Ctrl+S) ការបោះពុម្ពនៅតែប្រើគំរូបច្ចុប្បន្ន");

            // ---- Title, status, layer list ----
            A("Untitled design", "ការរចនាគ្មានឈ្មោះ");
            A("“{0}”  —  move, resize and edit anything on the front and back",
                "“{0}”  —  ផ្លាស់ទី ប្ដូរទំហំ និងកែអ្វីៗលើផ្នែកមុខ និងក្រោយ");
            A("“{0}”  ·  new, not saved yet  —  move, resize and edit anything on the front and back",
                "“{0}”  ·  ថ្មី មិនទាន់រក្សាទុក  —  ផ្លាស់ទី ប្ដូរទំហំ និងកែអ្វីៗលើផ្នែកមុខ និងក្រោយ");
            A("Save the changes to “{0}” first?", "រក្សាទុកការផ្លាស់ប្ដូរទៅ “{0}” ជាមុនសិន?");
            A("New design “{0}” from the starter layout. Change anything, then click Save.",
                "ការរចនាថ្មី “{0}” ពីប្លង់ចាប់ផ្ដើម។ ផ្លាស់ប្ដូរអ្វីក៏បាន រួចចុច រក្សាទុក។");
            A("New blank design “{0}”. Add text, fields, photo, QR, images and shapes from the left, then click Save.",
                "ការរចនាទទេថ្មី “{0}”។ បន្ថែមអត្ថបទ វាល រូបថត QR រូបភាព និងរាងពីខាងឆ្វេង រួចចុច រក្សាទុក។");
            A("Copy created as “{0}”. Click Save to keep it.", "បានបង្កើតច្បាប់ចម្លងជា “{0}”។ ចុច រក្សាទុក ដើម្បីរក្សាវា។");
            A("Name shown in Card Template and in this list", "ឈ្មោះដែលបង្ហាញក្នុង គំរូកាត និងក្នុងបញ្ជីនេះ");
            A("Deleted “{0}”.", "បានលុប “{0}”។");
            A("MY DESIGNS", "ការរចនារបស់ខ្ញុំ");
            A("(not saved)", "(មិនទាន់រក្សាទុក)");
            A("Duplicate this design", "ចម្លងការរចនានេះមួយទៀត");
            A("Delete this design…", "លុបការរចនានេះ…");
            A("START OVER (undo brings it back)", "ចាប់ផ្ដើមឡើងវិញ (មិនធ្វើវិញ នាំវាត្រឡប់មកវិញ)");
            A("Import design…", "នាំចូលការរចនា…");
            A("Export this design…", "នាំចេញការរចនានេះ…");
            A("Blank card\tCtrl+N", "កាតទទេ\tCtrl+N");
            A("From the starter layout", "ពីប្លង់ចាប់ផ្ដើម");
            A("Copy of “{0}”", "ច្បាប់ចម្លងនៃ “{0}”");
            A("BACK OF THE CARD", "ផ្នែកក្រោយនៃកាត");
            A("FRONT OF THE CARD", "ផ្នែកមុខនៃកាត");
            A("  ·  NOTHING SELECTED", "  ·  មិនទាន់ជ្រើសអ្វី");
            A("{0} PROPERTIES", "លក្ខណៈ{0}");
            A("Could not draw the card: {0}", "មិនអាចគូរកាតបានទេ៖ {0}");
            A("Click an element to select it  ·  drag to move, handles to resize (Shift keeps the shape)  ·  arrows nudge 0.1 mm (Shift 1 mm)  ·  right-click for more",
                "ចុចលើធាតុដើម្បីជ្រើស  ·  អូសដើម្បីផ្លាស់ទី ចំណុចជ្រុងដើម្បីប្ដូរទំហំ (Shift រក្សារាង)  ·  ព្រួញរំកិល 0.1 mm (Shift 1 mm)  ·  ចុចស្ដាំសម្រាប់ច្រើនទៀត");
            A("   ·  locked", "   ·  បានចាក់សោ");
            A("   ·  student fields print empty on the back", "   ·  វាលនិស្សិតបោះពុម្ពទទេនៅផ្នែកក្រោយ");
            A("  · locked", "  · បានចាក់សោ");
            A("  hidden", "  លាក់");
            A("Blank card", "កាតទទេ");
            A("Add Text, Field, Image, Photo, QR or Shape from the panel on the left",
                "បន្ថែម អត្ថបទ វាល រូបភាព រូបថត QR ឬរាង ពីផ្ទាំងខាងឆ្វេង");
            // Element kinds and default names
            A("Field", "វាល");
            A("Image", "រូបភាព");
            A("QR", "QR");
            A("Shape", "រាង");
            A("(empty text)", "(អត្ថបទទទេ)");
            A("(no column)", "(គ្មានជួរឈរ)");
            A("(no file)", "(គ្មានឯកសារ)");
            A("Student photo", "រូបថតនិស្សិត");
            A("QR: {0}", "QR៖ {0}");
            A("Pasted picture", "រូបភាពដែលបានបិទភ្ជាប់");

            // ---- Images ----
            A("Upload image to the card", "ផ្ទុករូបភាពឡើងទៅកាត");
            A("These files are not pictures the card can use:", "ឯកសារទាំងនេះមិនមែនជារូបភាពដែលកាតអាចប្រើបានទេ៖");
            A("Add image", "បន្ថែមរូបភាព");
            A("Could not paste the picture: {0}", "មិនអាចបិទភ្ជាប់រូបភាពបានទេ៖ {0}");
            A("Change picture", "ប្ដូររូបភាព");
            A("Could not use this file:", "មិនអាចប្រើឯកសារនេះបានទេ៖");

            // ---- Menus ----
            A("STUDENT", "និស្សិត");
            A("Student ID  ·  អត្តលេខ", "អត្តលេខ  ·  Student ID");
            A("Name (Khmer)  ·  គោត្តនាម-នាម", "គោត្តនាម-នាម  ·  Name (Khmer)");
            A("Name (Latin)  ·  អក្សរឡាតាំង", "អក្សរឡាតាំង  ·  Name (Latin)");
            A("Gender  ·  ភេទ", "ភេទ  ·  Gender");
            A("Date of birth  ·  ថ្ងៃខែឆ្នាំកំណើត", "ថ្ងៃខែឆ្នាំកំណើត  ·  Date of birth");
            A("Faculty  ·  មហាវិទ្យាល័យ", "មហាវិទ្យាល័យ  ·  Faculty");
            A("Level  ·  កម្រិត", "កម្រិត  ·  Level");
            A("Generation  ·  ជំនាន់ទី", "ជំនាន់ទី  ·  Generation");
            A("Expiry date  ·  ផុតកំណត់", "ផុតកំណត់  ·  Expiry date");
            A("ANY COLUMN OF YOUR LIST", "ជួរឈរណាមួយក្នុងបញ្ជីរបស់អ្នក");
            A("Custom field (any column)…", "វាលផ្ទាល់ខ្លួន (ជួរឈរណាមួយ)…");
            A("Print a column the app does not know, e.g. Phone, BloodType, Department",
                "បោះពុម្ពជួរឈរដែលកម្មវិធីមិនស្គាល់ ឧ. Phone, BloodType, Department");
            A("CARD TEXT (same on every card)", "អត្ថបទលើកាត (ដូចគ្នាលើកាតគ្រប់សន្លឹក)");
            A("YOUR OWN PICTURE", "រូបភាពផ្ទាល់ខ្លួន");
            A("Upload image…", "ផ្ទុករូបភាពឡើង…");
            A("Paste copied picture\tCtrl+V", "បិទភ្ជាប់រូបភាពដែលបានចម្លង\tCtrl+V");
            A("Tip: you can also drag picture files onto the card", "គន្លឹះ៖ អ្នកក៏អាចអូសឯកសាររូបភាពមកលើកាតបានដែរ");
            A("BUILT-IN", "មានស្រាប់");
            A("NPIC logo", "ឡូហ្គោ NPIC");
            A("Ministry logo", "ឡូហ្គោក្រសួង");
            A("Stamp", "ត្រា");
            A("Director signature (Card Text)", "ហត្ថលេខានាយក (អត្ថបទលើកាត)");
            A("Location icon", "រូបតំណាងទីតាំង");
            A("Email icon", "រូបតំណាងអ៊ីមែល");
            A("Website icon", "រូបតំណាងគេហទំព័រ");
            A("Student QR (changes per student)", "QR និស្សិត (ប្ដូរតាមនិស្សិតម្នាក់ៗ)");
            A("Back QR (from Card Text)", "QR ផ្នែកក្រោយ (ពីអត្ថបទលើកាត)");
            A("QR with my own text…", "QR ដែលមានអត្ថបទផ្ទាល់ខ្លួន…");
            A("Rectangle / band", "ចតុកោណ / ឆ្នូត");
            A("Circle / ellipse", "រង្វង់ / អេលីប");
            A("Line", "បន្ទាត់");
            A("Replace both sides with the starter layout", "ជំនួសផ្នែកទាំងពីរដោយប្លង់ចាប់ផ្ដើម");
            A("Replace both sides of “{0}” with the starter layout?", "ជំនួសផ្នែកទាំងពីររបស់ “{0}” ដោយប្លង់ចាប់ផ្ដើម?");
            A("Undo (Ctrl+Z) brings your design back.", "មិនធ្វើវិញ (Ctrl+Z) នាំការរចនារបស់អ្នកត្រឡប់មកវិញ។");
            A("Empty the {0}", "សម្អាតផ្នែក{0}");
            A("Remove everything from the {0}?", "ដកអ្វីៗទាំងអស់ចេញពីផ្នែក{0}?");
            A("Undo (Ctrl+Z) brings it back.", "មិនធ្វើវិញ (Ctrl+Z) នាំវាត្រឡប់មកវិញ។");
            A("Bring to front", "នាំទៅមុខបំផុត");
            A("Bring forward\tCtrl+]", "នាំមកមុខ\tCtrl+]");
            A("Send backward\tCtrl+[", "បញ្ជូនទៅក្រោយ\tCtrl+[");
            A("Send to back", "បញ្ជូនទៅក្រោយបំផុត");
            A("Duplicate\tCtrl+D", "ចម្លងមួយទៀត\tCtrl+D");
            A("Copy\tCtrl+C", "ចម្លង\tCtrl+C");
            A("Paste\tCtrl+V", "បិទភ្ជាប់\tCtrl+V");
            A("Change picture…", "ប្ដូររូបភាព…");
            A("Unlock", "ដោះសោ");
            A("Lock", "ចាក់សោ");
            A("Centre horizontally", "តម្រឹមកណ្ដាលផ្ដេក");
            A("Centre vertically", "តម្រឹមកណ្ដាលបញ្ឈរ");
            A("Delete\tDel", "លុប\tDel");

            // ---- Save, import, export ----
            A("“{0}” saved.  Pick it in Card Template… (or click Save & Use) to print with it.",
                "បានរក្សាទុក “{0}”។  ជ្រើសវាក្នុង គំរូកាត… (ឬចុច រក្សាទុក និងប្រើ) ដើម្បីបោះពុម្ពជាមួយវា។");
            A("Import card design", "នាំចូលការរចនាកាត");
            A("Imported {0} as “{1}”. Click Save to keep it.", "បាននាំចូល {0} ជា “{1}”។ ចុច រក្សាទុក ដើម្បីរក្សាវា។");
            A("Export card design", "នាំចេញការរចនាកាត");
            A("Exported to {0}. Pictures chosen with From file… stay on this PC: copy them along if you move the design.",
                "បាននាំចេញទៅ {0}។ រូបភាពដែលបានជ្រើសដោយ ពីឯកសារ… នៅលើកុំព្យូទ័រនេះ៖ ចម្លងវាទៅជាមួយ ប្រសិនបើអ្នកផ្លាស់ទីការរចនា។");
            A("Could not export the design:", "មិនអាចនាំចេញការរចនាបានទេ៖");
            A("Close without saving?", "បិទដោយមិនរក្សាទុក?");
            A("Your changes to “{0}” will be lost.", "ការផ្លាស់ប្ដូររបស់អ្នកទៅ “{0}” នឹងបាត់បង់។");
            A("This is not a Card Studio custom card design:", "នេះមិនមែនជាការរចនាកាតផ្ទាល់ខ្លួនរបស់ Card Studio ទេ៖");

            // ---- Custom field and design name popups ----
            A("Custom field", "វាលផ្ទាល់ខ្លួន");
            A("Print any column of your student list (Excel, CSV, Google Sheet, API or SQL)",
                "បោះពុម្ពជួរឈរណាមួយនៃបញ្ជីនិស្សិតរបស់អ្នក (Excel, CSV, Google Sheet, API ឬ SQL)");
            A("Column  (header of the column in your list — pick one or type it)", "ជួរឈរ  (ក្បាលជួរឈរក្នុងបញ្ជីរបស់អ្នក — ជ្រើស ឬវាយវា)");
            A("Label printed in front  (optional, e.g. លេខទូរស័ព្ទ)", "ស្លាកដែលបោះពុម្ពនៅខាងមុខ  (ស្រេចចិត្ត ឧ. លេខទូរស័ព្ទ)");
            A("Add Field", "បន្ថែមវាល");
            A("Type or pick the column to print.", "វាយ ឬជ្រើសជួរឈរដែលត្រូវបោះពុម្ព។");
            A("No student list loaded: type the column header exactly as it is in your list.",
                "មិនទាន់ផ្ទុកបញ្ជីនិស្សិត៖ វាយក្បាលជួរឈរឱ្យដូចក្នុងបញ្ជីរបស់អ្នកបេះបិទ។");
            A("Example ({0}):  {1}", "ឧទាហរណ៍ ({0})៖  {1}");
            A("(empty)", "(ទទេ)");
            A("Shown as [{0}] in the designer until a list with this column is loaded.",
                "បង្ហាញជា [{0}] ក្នុងកម្មវិធីរចនា រហូតដល់បញ្ជីដែលមានជួរឈរនេះត្រូវបានផ្ទុក។");
            A("The loaded list has no column “{0}”: it prints empty until a list with it is loaded.",
                "បញ្ជីដែលបានផ្ទុកគ្មានជួរឈរ “{0}” ទេ៖ វាបោះពុម្ពទទេ រហូតដល់បញ្ជីដែលមានវាត្រូវបានផ្ទុក។");
            A("Design name", "ឈ្មោះការរចនា");

            // ---- Element properties (Rendering\CustomCard.cs) ----
            A("1. Layer", "1. ស្រទាប់");
            A("2. Position (mm)", "2. ទីតាំង (mm)");
            A("3. Image", "3. រូបភាព");
            A("3. Photo", "3. រូបថត");
            A("3. QR code", "3. កូដ QR");
            A("3. Shape", "3. រាង");
            A("3. Text", "3. អត្ថបទ");
            A("4. Label", "4. ស្លាក");
            A("Card side", "ផ្នែកកាត");
            A("Alignment", "ការតម្រឹម");
            A("Background colour", "ពណ៌ផ្ទៃខាងក្រោយ");
            A("Bold Latin", "អក្សរឡាតាំងដិត");
            A("Border (mm)", "ស៊ុម (mm)");
            A("Border colour", "ពណ៌ស៊ុម");
            A("Colour", "ពណ៌");
            A("Column", "ជួរឈរ");
            A("Content", "ខ្លឹមសារ");
            A("Corner radius (mm)", "កាំជ្រុង (mm)");
            A("Custom text", "អត្ថបទផ្ទាល់ខ្លួន");
            A("Fill colour", "ពណ៌បំពេញ");
            A("Font", "ពុម្ពអក្សរ");
            A("Format", "ទម្រង់");
            A("Frame (mm)", "ស៊ុម (mm)");
            A("Frame colour", "ពណ៌ស៊ុម");
            A("Height", "កម្ពស់");
            A("Width", "ទទឹង");
            A("Label colour", "ពណ៌ស្លាក");
            A("Label column (mm)", "ជួរឈរស្លាក (mm)");
            A("Label", "ស្លាក");
            A("Locked", "បានចាក់សោ");
            A("Opacity (%)", "ភាពស្រអាប់ (%)");
            A("Remove white background", "លុបផ្ទៃខាងក្រោយពណ៌ស");
            A("Size (pt)", "ទំហំ (pt)");
            A("Visible", "បង្ហាញ");
            A("0 = label and value on one line (\"Label: value\"). More than 0 = the label gets a column this wide and \": value\" starts after it, so rows line up.",
                "0 = ស្លាក និងតម្លៃនៅលើបន្ទាត់តែមួយ (\"ស្លាក: តម្លៃ\")។ លើសពី 0 = ស្លាកមានជួរឈរទទឹងប៉ុណ្ណេះ ហើយ \": តម្លៃ\" ចាប់ផ្ដើមបន្ទាប់ពីវា ដើម្បីឱ្យជួរតម្រឹមគ្នា។");
            A("100 = solid. Use 5–15 for a faded watermark.", "100 = ដិតពេញ។ ប្រើ 5–15 សម្រាប់សញ្ញាទឹកស្រាលៗ។");
            A("A locked element cannot be moved with the mouse (handy for backgrounds). Select it in the layer list to edit it.",
                "ធាតុដែលបានចាក់សោមិនអាចផ្លាស់ទីដោយកណ្ដុរបានទេ (ងាយស្រួលសម្រាប់ផ្ទៃខាងក្រោយ)។ ជ្រើសវាក្នុងបញ្ជីស្រទាប់ ដើម្បីកែវា។");
            A("AsIs = as in the list. Uppercase = CAPITALS. KhmerDigits = 2025 → ២០២៥. KhmerDate = 24/01/2008 → ២៤ មករា ២០០៨.",
                "ដដែល = ដូចក្នុងបញ្ជី។ អក្សរធំ = CAPITALS។ លេខខ្មែរ = 2025 → ២០២៥។ កាលបរិច្ឆេទខ្មែរ = 24/01/2008 → ២៤ មករា ២០០៨។");
            A("Built-in picture, or CustomFile to use the picture chosen in File. Signature = the director signature from Card Text.",
                "រូបភាពមានស្រាប់ ឬ ឯកសារផ្ទាល់ខ្លួន ដើម្បីប្រើរូបភាពដែលបានជ្រើសក្នុង ឯកសារ។ ហត្ថលេខា = ហត្ថលេខានាយកពីអត្ថបទលើកាត។");
            A("Colour of the whole side, under every element.", "ពណ៌នៃផ្នែកទាំងមូល នៅក្រោមធាតុទាំងអស់។");
            A("Data printed here. Student fields come from the student list; Issue lines, Director, Address, Email and Website from Card Text. " +
              "Custom = any column of the student list, named in Column. On the back, student fields print empty (the back is the same for every student).",
                "ទិន្នន័យដែលបោះពុម្ពនៅទីនេះ។ វាលនិស្សិតមកពីបញ្ជីនិស្សិត បន្ទាត់ចេញប័ណ្ណ នាយក អាសយដ្ឋាន អ៊ីមែល និងគេហទំព័រ មកពីអត្ថបទលើកាត។ " +
                "ផ្ទាល់ខ្លួន = ជួរឈរណាមួយនៃបញ្ជីនិស្សិត ដែលដាក់ឈ្មោះក្នុង ជួរឈរ។ នៅផ្នែកក្រោយ វាលនិស្សិតបោះពុម្ពទទេ (ផ្នែកក្រោយដូចគ្នាសម្រាប់និស្សិតគ្រប់រូប)។");
            A("Distance from the left edge of the card, in mm (card is 84.6 mm wide).", "ចម្ងាយពីគែមឆ្វេងនៃកាត គិតជា mm (កាតទទឹង 84.6 mm)។");
            A("Distance from the top edge of the card, in mm (card is 54 mm high).", "ចម្ងាយពីគែមខាងលើនៃកាត គិតជា mm (កាតកម្ពស់ 54 mm)។");
            A("Font of the Khmer letters. Latin letters and digits always use Times New Roman at the same size.",
                "ពុម្ពអក្សរនៃអក្សរខ្មែរ។ អក្សរឡាតាំង និងលេខ តែងតែប្រើ Times New Roman ទំហំដូចគ្នា។");
            A("Frame thickness around the photo; 0 = no frame.", "កម្រាស់ស៊ុមជុំវិញរូបថត 0 = គ្មានស៊ុម។");
            A("Hidden elements are not printed.", "ធាតុដែលលាក់មិនត្រូវបានបោះពុម្ពទេ។");
            A("Line: horizontal when wider than high, otherwise vertical. Its thickness is Border (mm). " +
              "Triangle and Curve fill the box and point toward Direction.",
                "បន្ទាត់៖ ផ្ដេកពេលទទឹងធំជាងកម្ពស់ បើមិនដូច្នោះទេ បញ្ឈរ។ កម្រាស់របស់វាគឺ ស៊ុម (mm)។ " +
                "ត្រីកោណ និង ខ្សែកោង ពេញប្រអប់ ហើយចង្អុលទៅតាម ទិស។");
            A("Triangle and Curve only. Up / Down / Left / Right = the apex or the top of the arch is on that side. " +
              "A corner (TopLeft…) = a right-angled triangle or a quarter circle sitting in that corner. " +
              "A curve is drawn with Border colour and Border (mm); give it a Fill colour to fill the area inside the arch.",
                "សម្រាប់តែ ត្រីកោណ និង ខ្សែកោង។ ឡើងលើ / ចុះក្រោម / ឆ្វេង / ស្ដាំ = កំពូល ឬចំណុចខ្ពស់នៃធ្នូ នៅខាងនោះ។ " +
                "ជ្រុង (ជ្រុងលើឆ្វេង…) = ត្រីកោណកែង ឬមួយភាគបួនរង្វង់ ដែលនៅក្នុងជ្រុងនោះ។ " +
                "ខ្សែកោងគូរដោយ ពណ៌ស៊ុម និង ស៊ុម (mm) ដាក់ ពណ៌បំពេញ ដើម្បីបំពេញផ្ទៃក្នុងធ្នូ។");
            A("Makes white pixels transparent (logos, stamp, signature).", "ធ្វើឱ្យភីកសែលពណ៌សថ្លា (ឡូហ្គោ ត្រា ហត្ថលេខា)។");
            A("Optional text in front of the value, e.g. អត្តលេខ or ភេទ. Empty = value only.",
                "អត្ថបទស្រេចចិត្តនៅខាងមុខតម្លៃ ឧ. អត្តលេខ ឬ ភេទ។ ទទេ = តែតម្លៃ។");
            A("Printed as typed, on one line.", "បោះពុម្ពដូចដែលបានវាយ លើបន្ទាត់តែមួយ។");
            A("Rounded corners of a rectangle.", "ជ្រុងមូលនៃចតុកោណ។");
            A("Shown in the layer list only; never printed.", "បង្ហាញតែក្នុងបញ្ជីស្រទាប់ មិនដែលបោះពុម្ពទេ។");
            A("StudentQr changes per student. The QR is square: it uses the smaller of Width and Height.",
                "QR និស្សិតប្ដូរតាមនិស្សិតម្នាក់ៗ។ QR ជាការ៉េ៖ វាប្រើតម្លៃតូចជាងរវាង ទទឹង និង កម្ពស់។");
            A("Text size. A line that is wider than the box shrinks (down to 60%) to fit.",
                "ទំហំអក្សរ។ បន្ទាត់ដែលធំជាងប្រអប់នឹងរួមតូច (រហូតដល់ 60%) ដើម្បីឱ្យសម។");
            A("Times New Roman Bold for the Latin letters (Khmer fonts have no bold).",
                "Times New Roman ដិត សម្រាប់អក្សរឡាតាំង (ពុម្ពអក្សរខ្មែរគ្មានដិតទេ)។");
            A("Transparent (Web tab) = no border.", "Transparent (ផ្ទាំង Web) = គ្មានស៊ុម។");
            A("Transparent (Web tab) = no fill.", "Transparent (ផ្ទាំង Web) = គ្មានការបំពេញ។");
            A("Used when Content is CustomText. Keep it short; a long text makes a dense code.",
                "ប្រើពេល ខ្លឹមសារ ជា អត្ថបទផ្ទាល់ខ្លួន។ សរសេរខ្លី អត្ថបទវែងធ្វើឱ្យកូដក្រាស់។");
            A("Used when Field is Custom: the column header in your Excel / CSV / Google Sheet / SQL list, e.g. Phone or BloodType " +
              "(capital letters do not matter). Pick one from the list or type it.",
                "ប្រើពេល វាល ជា ផ្ទាល់ខ្លួន៖ ក្បាលជួរឈរក្នុងបញ្ជី Excel / CSV / Google Sheet / SQL របស់អ្នក ឧ. Phone ឬ BloodType " +
                "(អក្សរធំ ឬតូចមិនសំខាន់)។ ជ្រើសមួយពីបញ្ជី ឬវាយវា។");
            A("Used when Image is CustomFile. Click … to choose a PNG/JPG; it is copied into the app's settings folder.",
                "ប្រើពេល រូបភាព ជា ឯកសារផ្ទាល់ខ្លួន។ ចុច … ដើម្បីជ្រើស PNG/JPG វាត្រូវបានចម្លងចូលថតការកំណត់របស់កម្មវិធី។");
            // Choices (enum values shown in the property list)
            A("Left", "ឆ្វេង");
            A("Center", "កណ្ដាល");
            A("Right", "ស្ដាំ");
            A("AsIs", "ដដែល");
            A("Uppercase", "អក្សរធំ");
            A("KhmerDigits", "លេខខ្មែរ");
            A("KhmerDate", "កាលបរិច្ឆេទខ្មែរ");
            A("NameKhmer", "ឈ្មោះខ្មែរ");
            A("NameLatin", "ឈ្មោះឡាតាំង");
            A("Gender", "ភេទ");
            A("DateOfBirth", "ថ្ងៃខែឆ្នាំកំណើត");
            A("ExpiryDate", "កាលបរិច្ឆេទផុតកំណត់");
            A("IssueLine1", "បន្ទាត់ចេញប័ណ្ណ 1");
            A("IssueLine2", "បន្ទាត់ចេញប័ណ្ណ 2");
            A("DirectorTitle", "តួនាទីនាយក");
            A("DirectorName", "ឈ្មោះនាយក");
            A("NpicLogo", "ឡូហ្គោ NPIC");
            A("MinistryLogo", "ឡូហ្គោក្រសួង");
            A("Signature", "ហត្ថលេខា");
            A("BackQrPicture", "រូប QR ផ្នែកក្រោយ");
            A("PinIcon", "រូបតំណាងទីតាំង");
            A("MailIcon", "រូបតំណាងអ៊ីមែល");
            A("GlobeIcon", "រូបតំណាងគេហទំព័រ");
            A("CustomFile", "ឯកសារផ្ទាល់ខ្លួន");
            A("StudentQr", "QR និស្សិត");
            A("BackQr", "QR ផ្នែកក្រោយ");
            A("CustomText", "អត្ថបទផ្ទាល់ខ្លួន");
            A("Rectangle", "ចតុកោណ");
            A("Ellipse", "អេលីប");
            A("Rotation (°)", "បង្វិល (°)");
            A("Turns the shape clockwise around its centre, 0–359°. On the card, drag the round handle above the selection " +
              "(Shift = 15° steps), or right-click → Rotate 90°.",
                "បង្វិលរាងតាមទិសទ្រនិចនាឡិកាជុំវិញចំណុចកណ្ដាលរបស់វា 0–359°។ នៅលើកាត អូសចំណុចមូលខាងលើការជ្រើសរើស " +
                "(Shift = ម្ដង 15°) ឬចុចស្ដាំ → បង្វិល 90°។");
            A("Rotate 90° right", "បង្វិល 90° ទៅស្ដាំ");
            A("Rotate 90° left", "បង្វិល 90° ទៅឆ្វេង");
            A("Reset rotation", "កំណត់ការបង្វិលឡើងវិញ");
            A("Triangle", "ត្រីកោណ");
            A("Curve", "ខ្សែកោង");
            A("Direction", "ទិស");
            A("Up", "ឡើងលើ");
            A("Down", "ចុះក្រោម");
            A("TopLeft", "ជ្រុងលើឆ្វេង");
            A("TopRight", "ជ្រុងលើស្ដាំ");
            A("BottomLeft", "ជ្រុងក្រោមឆ្វេង");
            A("BottomRight", "ជ្រុងក្រោមស្ដាំ");
            A("Generate", "បង្កើត");
        }
    }
}
