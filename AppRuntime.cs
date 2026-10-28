// v1.3.0

using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using WinUiShared;

namespace App1
{
    /// <summary>
    /// プロセス寿命: トレイ・ガンマ・二重起動イベント。MainWindow は都度生成（ADM 同等）。
    /// 共通処理（二重起動・トレイ・復帰監視・終了の一本化）は WinUiShared.AppRuntimeBase。
    /// ここにはガンマ（色温度）の適用・スケジュール・復帰時の再適用だけを書く。
    /// </summary>
    public sealed class AppRuntime : AppRuntimeBase
    {
        private readonly Settings _settings;
        private readonly ObservableCollection<Pattern> _patterns;
        private readonly AppState _appState;
        private readonly GammaTransitionService _gammaTransition = new();
        private readonly DispatcherTimer _gammaScheduleTimer = new();

        private bool _gammaInitialized;
        private bool _gammaPreviewActive;

        public AppRuntime(Application app, Settings settings)
            : base(app, settings)
        {
            _settings = settings;
            _patterns = new ObservableCollection<Pattern>(_settings.Patterns.OrderBy(p => p.Time));
            _appState = new AppState(_settings, _patterns);
            WireAppStateGammaHooks();
        }

        public AppState AppState => _appState;
        public Settings Settings => _settings;

        protected override Window CreateMainWindow() => new MainWindow(this);

        /// <summary>コア（ガンマ）をトレイより先に初期化する（SPM 同型・ログオン直後の未適用を防ぐ）。</summary>
        protected override void OnStartCore()
        {
            RegisterGammaResetOnExit();
            EnsureGamma();
        }

        /// <summary>終了時（窓を閉じた後）。タイマーと遷移を止め、画面のガンマを元に戻す。</summary>
        protected override void OnExitCore()
        {
            _gammaScheduleTimer.Stop();
            _gammaTransition.Stop();
            GammaController.ResetGamma();
        }

        private void WireAppStateGammaHooks()
        {
            _appState.ApplyTrayIconVisibility = ApplyTrayIconVisibility;
            _appState.SavePatterns = () => { };
            _appState.PreviewGamma = settings =>
            {
                _gammaPreviewActive = true;
                _gammaTransition.ApplyImmediate(settings);
            };
            _appState.RefreshGamma = () =>
            {
                _gammaPreviewActive = false;
                ApplyCurrentGamma();
            };
            _appState.RescheduleTimer = ScheduleNextGammaCheck;
        }

        private void EnsureGamma()
        {
            if (_gammaInitialized)
                return;

            _gammaInitialized = true;
            _gammaScheduleTimer.Tick += (_, _) =>
            {
                if (_gammaPreviewActive)
                    return;
                ApplyCurrentGamma();
                ScheduleNextGammaCheck();
            };

            _gammaTransition.ResetToOff(applyToDisplay: true);
            ResumeCoordinator?.BeginStartupPeriod();
            ScheduleNextGammaCheck();
        }

        protected override void OnResumeApplyCore()
        {
            if (!_gammaInitialized)
                return;

            try
            {
                _gammaPreviewActive = false;
                RestartGammaTimers();
                ApplyCurrentGamma(forceReapply: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnResumeApply failed: {ex.Message}");
            }
        }

        /// <summary>UI スレッドへ積めなかったとき（任意スレッド）。UI 更新なしで期待ガンマを Force 適用。</summary>
        protected override void OnResumeApplyDirectCore()
        {
            if (!_gammaInitialized)
                return;

            _gammaPreviewActive = false;
            ForceApplyExpectedGammaDirect();
        }

        protected override bool UseResumeWatchdog => true;

        protected override void OnResumeWatchdogCore()
        {
            if (!_gammaInitialized)
                return;

            EnsureGammaApplied();
            RestartGammaTimers();
        }

        /// <summary>期待ガンマがまだ画面に載っていなければ true（復帰後、載るまで最大 180 秒 Force を続ける）。</summary>
        protected override bool UseContinueForcing => true;

        protected override bool NeedsContinueForcing()
        {
            if (IsExitingProcess || !_gammaInitialized || _gammaPreviewActive)
                return false;
            if (!TryGetExpectedGammaSettings(out var expected))
                return false;
            return !GammaController.IsLikelyApplied(expected);
        }

        /// <summary>UI 更新なしで期待ガンマを ForceApply（任意スレッド可）。</summary>
        private void ForceApplyExpectedGammaDirect()
        {
            try
            {
                if (!TryGetExpectedGammaSettings(out var expected))
                {
                    if (!_settings.IsFilterEnabled || !_patterns.Any())
                        _gammaTransition.ForceApply(GammaSettings.Off);
                    return;
                }

                _gammaTransition.ForceApply(expected);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ForceApplyExpectedGammaDirect failed: {ex.Message}");
            }
        }

        /// <summary>スリープ復帰後などにスケジュール用 DispatcherTimer を起こす。</summary>
        private void RestartGammaTimers()
        {
            ScheduleNextGammaCheck();
        }

        private void EnsureGammaApplied()
        {
            if (IsExitingProcess || !_gammaInitialized || _gammaPreviewActive)
                return;

            if (ResumeCoordinator?.ShouldForceApply == true)
            {
                ApplyCurrentGamma(forceReapply: true);
                return;
            }

            if (!TryGetExpectedGammaSettings(out var expected))
                return;

            if (GammaController.IsLikelyApplied(expected))
                return;

            _gammaTransition.ForceApply(expected);
        }

        private bool TryGetExpectedGammaSettings(out GammaSettings settings)
        {
            settings = GammaSettings.Off;

            if (!_settings.IsFilterEnabled || !_patterns.Any())
                return false;

            var currentPattern = ScheduleHelper.ResolveActivePattern(_patterns, DateTime.Now);
            if (currentPattern == null)
                return false;

            settings = GammaSettings.FromPattern(currentPattern);
            return true;
        }

        private void ScheduleNextGammaCheck()
        {
            _gammaScheduleTimer.Stop();
            var delay = ScheduleHelper.GetDelayUntilNextTransition(_patterns, DateTime.Now);
            _gammaScheduleTimer.Interval = delay ?? TimeSpan.FromHours(1);
            _gammaScheduleTimer.Start();
        }

        private void ApplyCurrentGamma(bool forceReapply = false)
        {
            try
            {
                ApplyCurrentGammaCore(forceReapply);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyCurrentGamma failed: {ex.Message}");
            }
        }

        private void ApplyCurrentGammaCore(bool forceReapply)
        {
            if (_gammaPreviewActive)
                return;

            if (!_settings.IsFilterEnabled)
            {
                ApplyGamma(GammaSettings.Off, forceReapply);
                _appState.UpdateRuntimeStatus(
                    Strings.Get("Status_FilterDisabled"),
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
                    null,
                    null);
                return;
            }

            if (!_patterns.Any())
            {
                ApplyGamma(GammaSettings.Off, forceReapply);
                _appState.UpdateRuntimeStatus(
                    Strings.Get("Status_NoSchedule"),
                    Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
                    null,
                    null);
                return;
            }

            var currentPattern = ScheduleHelper.ResolveActivePattern(_patterns, DateTime.Now);
            if (currentPattern == null)
            {
                ApplyGamma(GammaSettings.Off, forceReapply);
                return;
            }

            var gamma = GammaSettings.FromPattern(currentPattern);
            ApplyGamma(gamma, forceReapply);
            _appState.UpdateRuntimeStatus(
                Strings.Format(
                    "Status_Applied",
                    gamma.Intensity,
                    gamma.ColorTemperatureKelvin,
                    currentPattern.TimeRangeDisplay),
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
                gamma,
                currentPattern);
        }

        private void ApplyGamma(GammaSettings settings, bool forceReapply)
        {
            if (forceReapply)
                _gammaTransition.ForceApply(settings);
            else
                _gammaTransition.AnimateTo(settings);
        }

        private static void RegisterGammaResetOnExit()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => GammaController.ResetGamma();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => GammaController.ResetGamma();
        }
    }
}
