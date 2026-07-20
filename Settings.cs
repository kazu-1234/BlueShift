using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace App1
{
    public class Settings
    {
        /// <summary>デフォルトはシステム連動。</summary>
        public AppThemePreference ThemePreference { get; set; } = AppThemePreference.System;

        public bool AutoStart { get; set; } = false;

        /// <summary>旧設定互換（ログオンタスク専用化後は未使用）。</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? UseLogonTask { get; set; }

        public bool IsFilterEnabled { get; set; } = true;
        public List<Pattern> Patterns { get; set; } = new List<Pattern>();

        public int WindowWidth { get; set; } = 960;
        public int WindowHeight { get; set; } = 680;

        /// <summary>未保存時は -1。次回起動で位置を復元する。</summary>
        public int WindowX { get; set; } = -1;

        /// <summary>未保存時は -1。次回起動で位置を復元する。</summary>
        public int WindowY { get; set; } = -1;

        /// <summary>前回終了時に最大化されていたか。</summary>
        public bool WindowMaximized { get; set; }

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
                    if (settings == null)
                        return new Settings();

                    settings.NormalizePatterns();
                    return settings;
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

        /// <summary>旧設定ファイルに色温度が無い場合などの正規化。</summary>
        public void NormalizePatterns()
        {
            foreach (var pattern in Patterns)
                pattern.NormalizeColorTemperature();
        }
    }
}
