using Microsoft.UI.Xaml;
using System;

namespace App1
{
    /// <summary>ガンマ強度・色温度の即時適用となめらかな遷移を管理する。</summary>
    public sealed class GammaTransitionService
    {
        private const int FrameIntervalMs = 16;
        private static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(800);

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

        /// <summary>スライダードラッグ等、即時反映する。</summary>
        public void ApplyImmediate(GammaSettings settings)
        {
            StopAnimation();
            _appliedSettings = settings.Clamp();
            ApplySettings(_appliedSettings);
        }

        /// <summary>OS によるガンマリセット後など、同一設定でも再適用する。</summary>
        public void ForceApply(GammaSettings settings)
        {
            StopAnimation();
            _appliedSettings = settings.Clamp();
            ApplySettings(_appliedSettings);
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

            _appliedSettings = current.Clamp();
            ApplySettings(_appliedSettings);

            if (progress >= 1.0)
                StopAnimation();
        }

        private void StopAnimation()
        {
            _timer.Stop();
            _isAnimating = false;
        }

        private static void ApplySettings(GammaSettings settings)
        {
            GammaController.SetGamma(settings);
        }
    }
}
