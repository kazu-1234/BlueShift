using System;
using System.Collections.Generic;
using System.IO;
using WinUiShared;

namespace App1
{
    public class Settings : IAppHostSettings
    {
        /// <summary>デフォルトはシステム連動。</summary>
        public AppThemePreference ThemePreference { get; set; } = AppThemePreference.System;

        public bool AutoStart { get; set; } = false;

        /// <summary>true のときタスクトレイアイコンを表示しない。</summary>
        public bool HideTrayIcon { get; set; }

        /// <summary>true のとき起動後に「常駐準備完了」のトレイ通知を出す（キーが無い既存設定は true＝従来どおり）。</summary>
        public bool ShowStartupNotification { get; set; } = true;

        /// <summary>true のときログオンタスク、false のときレジストリ Run（スタートアップ）。</summary>
        public bool UseLogonTask { get; set; } = true;

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

        /// <summary>起動時（初回 MainWindow 表示時）に更新を自動確認する。</summary>
        public bool AutoCheckUpdateOnStartup { get; set; } = true;

        /// <summary>最後に更新確認した UTC 時刻。未確認は null。</summary>
        public DateTime? LastUpdateCheckUtc { get; set; }

        private static string SettingsFilePath =>
            Path.Combine(AppSetup.Profile.AppDataDirectory, "settings.json");

        private static string LegacySettingsFilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "App1", "settings.json");

        public static Settings Load()
        {
            MigrateSettingsFileIfNeeded();

            // 読み込みは共通処理（壊れていれば 1 つ前の保存版 .bak で救済。どちらも無ければ null）
            var settings = JsonSettingsFile.Load<Settings>(SettingsFilePath);
            if (settings == null)
                return new Settings();

            settings.NormalizePatterns();
            return settings;
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

        /// <summary>保存（一時ファイル → 差し替えで、途中で落ちても壊れない）。失敗は無視する。</summary>
        public void Save() => JsonSettingsFile.Save(SettingsFilePath, this);

        /// <summary>旧設定ファイルに色温度が無い場合などの正規化。</summary>
        public void NormalizePatterns()
        {
            foreach (var pattern in Patterns)
                pattern.NormalizeColorTemperature();
        }
    }
}
