using System;
using System.IO;
using System.Xml.Serialization;

namespace CardStudio.Admin
{
    /// <summary>ADMIN_CARD's own window settings, in %AppData%\CardStudio.Admin\settings.xml.</summary>
    public sealed class AdminSettings
    {
        /// <summary>Dark (true) or light (false); null = the same as Card Studio on this PC. Read once at start-up.</summary>
        public bool? DarkMode { get; set; }

        /// <summary>Sidebar shows only icons.</summary>
        public bool SidebarCollapsed { get; set; }

        private static AdminSettings _current;

        /// <summary>The settings, read once.</summary>
        public static AdminSettings Current
        {
            get { return _current ?? (_current = Load()); }
        }

        private static string FilePath { get { return Path.Combine(Inventory.DataFolder, "settings.xml"); } }

        private static AdminSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var s = File.OpenRead(FilePath))
                        return (AdminSettings)new XmlSerializer(typeof(AdminSettings)).Deserialize(s);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException) { }
            return new AdminSettings();
        }

        /// <summary>Saves; a problem is ignored (only window preferences are lost).</summary>
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Inventory.DataFolder);
                using (var s = File.Create(FilePath)) new XmlSerializer(typeof(AdminSettings)).Serialize(s, this);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
        }
    }
}
