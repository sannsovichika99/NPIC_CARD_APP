namespace NPIC_CARD_APP
{
    // Activate Card Studio / License… window and the license texts (Services\LicenseService.cs).
    internal static partial class KhmerStrings
    {
        private static void Activation()
        {
            A("License", "អាជ្ញាប័ណ្ណ");
            A("Activate Card Studio", "ធ្វើឱ្យ Card Studio សកម្ម");
            A("A serial number is required to open the app", "ត្រូវការលេខសៀរៀល ដើម្បីបើកកម្មវិធី");
            A("MACHINE ID", "MACHINE ID");
            A("SERIAL NUMBER", "លេខសៀរៀល");
            A("Activate", "ធ្វើឱ្យសកម្ម");
            A("Exit", "ចាកចេញ");
            A("Copied", "បានចម្លង");
            A("The new serial number is saved.", "លេខសៀរៀលថ្មីត្រូវបានរក្សាទុក។");
            A("The saved serial number is not valid for this PC (it was made for another PC, or Windows was reinstalled). " +
              "Ask your administrator for a new serial for this Machine ID.",
                "លេខសៀរៀលដែលបានរក្សាទុកមិនត្រឹមត្រូវសម្រាប់កុំព្យូទ័រនេះទេ (វាត្រូវបានបង្កើតសម្រាប់កុំព្យូទ័រផ្សេង ឬ Windows ត្រូវបានដំឡើងឡើងវិញ)។ " +
                "សូមស្នើលេខសៀរៀលថ្មីសម្រាប់ Machine ID នេះពីអ្នកគ្រប់គ្រង។");
            A("Your serial number expired on {0}. Send this PC's Machine ID to your administrator for a new serial number.",
                "លេខសៀរៀលរបស់អ្នកបានផុតកំណត់នៅ {0}។ សូមផ្ញើ Machine ID របស់កុំព្យូទ័រនេះទៅអ្នកគ្រប់គ្រង ដើម្បីទទួលលេខសៀរៀលថ្មី។");
            A("This PC's date ({0}) is earlier than the last time Card Studio was used ({1}). Set the correct date and time in Windows, " +
              "then open Card Studio again. If the date is already correct, ask your administrator for a new serial number.",
                "កាលបរិច្ឆេទរបស់កុំព្យូទ័រនេះ ({0}) មុនពេលចុងក្រោយដែល Card Studio ត្រូវបានប្រើ ({1})។ កំណត់កាលបរិច្ឆេទ និងម៉ោងឱ្យត្រឹមត្រូវក្នុង Windows " +
                "រួចបើក Card Studio ម្ដងទៀត។ ប្រសិនបើកាលបរិច្ឆេទត្រឹមត្រូវរួចហើយ សូមស្នើលេខសៀរៀលថ្មីពីអ្នកគ្រប់គ្រង។");
            A("This serial never expires: a new one is only needed if the administrator asks for it.",
                "លេខសៀរៀលនេះមិនផុតកំណត់ទេ៖ ត្រូវការលេខថ្មី លុះត្រាតែអ្នកគ្រប់គ្រងស្នើ។");
            A("The current serial works until {0} (expires {1}).", "លេខសៀរៀលបច្ចុប្បន្នប្រើបានរហូតដល់ {0} (ផុតកំណត់ {1})។");

            // License status (System Info, Settings & Help → License…, window title)
            A("Activated · never expires", "សកម្ម · មិនផុតកំណត់");
            A("Activated · never expires.", "សកម្ម · មិនផុតកំណត់។");
            A("Activated until {0} (expires {1})", "សកម្មរហូតដល់ {0} (ផុតកំណត់ {1})");
            A("Activated until {0} (expires {1}).", "សកម្មរហូតដល់ {0} (ផុតកំណត់ {1})។");
            A("Expired on {0}", "បានផុតកំណត់នៅ {0}");
            A("Expired on {0}.", "បានផុតកំណត់នៅ {0}។");
            A("The PC's date is wrong (it is before {0})", "កាលបរិច្ឆេទរបស់កុំព្យូទ័រខុស (វាមុន {0})");
            A("The saved serial number is for another PC", "លេខសៀរៀលដែលបានរក្សាទុកគឺសម្រាប់កុំព្យូទ័រផ្សេង");
            A("Not activated", "មិនទាន់ធ្វើឱ្យសកម្ម");
            A("License expires {0}", "អាជ្ញាប័ណ្ណផុតកំណត់ {0}");

            // Checking a typed serial
            A("Enter the serial number.", "សូមបញ្ចូលលេខសៀរៀល។");
            A("This is not a Card Studio serial number: it should have {0} letters and digits (now {1}). Paste the whole serial, including the last group.",
                "នេះមិនមែនជាលេខសៀរៀល Card Studio ទេ៖ វាគួរមានអក្សរ និងលេខ {0} (ឥឡូវ {1})។ សូមបិទភ្ជាប់លេខសៀរៀលទាំងមូល រួមទាំងក្រុមចុងក្រោយ។");
            A("The serial number has a typo. Paste it again instead of typing it.", "លេខសៀរៀលមានកំហុសវាយ។ សូមបិទភ្ជាប់វាម្ដងទៀត ជំនួសឱ្យការវាយ។");
            A("This serial number is for another PC. Send this PC's Machine ID to your administrator to get its own serial.",
                "លេខសៀរៀលនេះសម្រាប់កុំព្យូទ័រផ្សេង។ សូមផ្ញើ Machine ID របស់កុំព្យូទ័រនេះទៅអ្នកគ្រប់គ្រង ដើម្បីទទួលលេខសៀរៀលផ្ទាល់ខ្លួន។");
            A("This serial number expired on {0}. Ask your administrator for a new one.", "លេខសៀរៀលនេះបានផុតកំណត់នៅ {0}។ សូមស្នើលេខថ្មីពីអ្នកគ្រប់គ្រង។");
            A("This PC's date ({0}) is earlier than the last time Card Studio was used ({1}). Set the correct date and time in Windows, then try again.",
                "កាលបរិច្ឆេទរបស់កុំព្យូទ័រនេះ ({0}) មុនពេលចុងក្រោយដែល Card Studio ត្រូវបានប្រើ ({1})។ កំណត់កាលបរិច្ឆេទ និងម៉ោងឱ្យត្រឹមត្រូវក្នុង Windows រួចព្យាយាមម្ដងទៀត។");
            A("The serial number is valid but could not be saved:", "លេខសៀរៀលត្រឹមត្រូវ ប៉ុន្តែមិនអាចរក្សាទុកបានទេ៖");
        }
    }
}
