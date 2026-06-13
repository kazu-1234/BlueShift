using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace App1
{
    public class Settings
    {
        public bool AutoStart { get; set; } = false;
        public bool IsFilterEnabled { get; set; } = true;
        public List<Pattern> Patterns { get; set; } = new List<Pattern>();

        private static string SettingsFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlueShift", "settings.json");

        private static string LegacySettingsFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "App1", "settings.json");

        public static Settings Load()
        {
            MigrateSettingsFileIfNeeded();

            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonConvert.DeserializeObject<Settings>(json);
                    return settings ?? new Settings();
                }
            }
            catch { }
            return new Settings();
        }

        private static void MigrateSettingsFileIfNeeded()
        {
            if (File.Exists(SettingsFilePath) || !File.Exists(LegacySettingsFilePath))
                return;

            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(LegacySettingsFilePath, SettingsFilePath, overwrite: false);
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }
    }
}