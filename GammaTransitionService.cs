using Microsoft.UI.Xaml;
using System;

namespace App1
{
    /// <summary>ガンマ強度・色温度の即時適用となめらかな遷移を管理する。</summary>
    public sealed class GammaTransitionService
    {
        private const int FrameIntervalMs = 16;
        private static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(2500);

        private readonly DispatcherTimer _timer;
        private GammaSettings _fromSettings;
        private GammaSettings _toSettings;
        private DateTime _startTime;
        private TimeSpan _duration;
        private GammaSettings _appliedSettings;
        private bool _isAnimating;

        public GammaTransitionService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(FrameIntervalMs)
            };
            _timer.Tick += Timer_Tick;
        }

        public GammaSettings AppliedSettings => _appliedSettings;

        public bool IsAnimating => _isAnimating;

        /// <summary>既定の遷移時間（起動・スケジュール切替で共有）。</summary>
        public static TimeSpan DefaultAnimationDuration => DefaultDuration;

        /// <summary>スライダードラッグ等、即時反映する。</summary>
        public void ApplyImmediate(GammaSettings settings)
        {
            StopAnimation();
            var clamped = settings.Clamp();
            if (ApplySettings(clamped))
                _appliedSettings = clamped;
        }

        /// <summary>OS によるガンマリセット後など、同一設定でも再適用する。</summary>
        /// <returns>GDI 適用に成功した場合 true。</returns>
        public bool ForceApply(GammaSettings settings)
        {
            StopAnimation();
            var clamped = settings.Clamp();
            if (!ApplySettings(clamped))
                return false;

            _appliedSettings = clamped;
            return true;
        }

        /// <summary>遷移状態を恒等（Off）に合わせ、必要なら実際のガンマもリセットする。</summary>
        public void ResetToOff(bool applyToDisplay = true)
        {
            StopAnimation();
            _appliedSettings = GammaSettings.Off;
            if (applyToDisplay)
                GammaController.ResetGamma();
        }

        /// <summary>指定強度のみへ遷移する（色温度は中立）。</summary>
        public void AnimateTo(int targetIntensity, TimeSpan? duration = null)
        {
            AnimateTo(new GammaSettings
            {
                Intensity = targetIntensity,
                ColorTemperatureKelvin = GammaSettings.DefaultColorTemperatureKelvin
            }, duration);
        }

        /// <summary>指定設定へなめらかに遷移する。</summary>
        public void AnimateTo(GammaSettings targetSettings, TimeSpan? duration = null)
        {
            targetSettings = targetSettings.Clamp();
            if (!_isAnimating && _appliedSettings.Equals(targetSettings))
                return;

            _fromSettings = _appliedSettings;
            _toSettings = targetSettings;
            _startTime = DateTime.UtcNow;
            _duration = duration ?? DefaultDuration;
            _isAnimating = true;
            _timer.Start();
        }

        public void Stop()
        {
            StopAnimation();
        }

        private void Timer_Tick(object? sender, object e)
        {
            var elapsed = DateTime.UtcNow - _startTime;
            double progress = _duration.TotalMilliseconds <= 0
                ? 1.0
                : Math.Min(1.0, elapsed.TotalMilliseconds / _duration.TotalMilliseconds);

            progress = progress * progress * (3.0 - 2.0 * progress);

            var current = new GammaSettings
            {
                Intensity = (int)Math.Round(
                    _fromSettings.Intensity + (_toSettings.Intensity - _fromSettings.Intensity) * progress),
                ColorTemperatureKelvin = (int)Math.Round(
                    _fromSettings.ColorTemperatureKelvin
                    + (_toSettings.ColorTemperatureKelvin - _fromSettings.ColorTemperatureKelvin) * progress)
            };

            var clamped = current.Clamp();
            // アニメ中は論理進捗を保持し、GDI 失敗時は次フレームで再試行する
            ApplySettings(clamped);
            _appliedSettings = clamped;

            if (progress >= 1.0)
                StopAnimation();
        }

        private void StopAnimation()
        {
            _timer.Stop();
            _isAnimating = false;
        }

        private static bool ApplySettings(GammaSettings settings)
        {
            return GammaController.SetGamma(settings);
        }
    }
}
