using System;
using System.Collections.Generic;
using System.IO;
using WinUiShared;

namespace App1
{
    public class Settings : AppHostSettingsBase
    {
        public bool IsFilterEnabled { get; set; } = true;
        public List<Pattern> Patterns { get; set; } = new List<Pattern>();

        private static string LegacySettingsFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "App1", "settings.json");

        public static Settings Load()
        {
            MigrateSettingsFileIfNeeded();

            // 読み込みは共通処理（壊れていれば 1 つ前の保存版 .bak で救済。どちらも無ければ null）
            var settings = JsonSettingsFile.Load<Settings>(GetDefaultPath());
            if (settings == null)
                return new Settings();

            settings.NormalizePatterns();
            return settings;
        }

        private static void MigrateSettingsFileIfNeeded()
        {
            if (File.Exists(GetDefaultPath()) || !File.Exists(LegacySettingsFilePath))
                return;

            try
            {
                var dir = Path.GetDirectoryName(GetDefaultPath());
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.Copy(LegacySettingsFilePath, GetDefaultPath(), overwrite: false);
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
