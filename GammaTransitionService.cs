using Microsoft.UI.Xaml;
using System;

namespace App1
{
    /// <summary>
    /// ガンマ強度の即時適用となめらかな遷移を管理する。
    /// </summary>
    public sealed class GammaTransitionService
    {
        private const int FrameIntervalMs = 16;
        private static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(800);

        private readonly DispatcherTimer _timer;
        private double _fromIntensity;
        private double _toIntensity;
        private DateTime _startTime;
        private TimeSpan _duration;
        private int _appliedIntensity;
        private bool _isAnimating;

        public GammaTransitionService()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(FrameIntervalMs)
            };
            _timer.Tick += Timer_Tick;
        }

        public int AppliedIntensity => _appliedIntensity;

        /// <summary>スライダープレビュー等、即時反映する。</summary>
        public void ApplyImmediate(int intensity)
        {
            StopAnimation();
            _appliedIntensity = Math.Clamp(intensity, 0, 100);
            ApplyIntensity(_appliedIntensity);
        }

        /// <summary>指定強度へなめらかに遷移する。0 でガンマをリセット。</summary>
        public void AnimateTo(int targetIntensity, TimeSpan? duration = null)
        {
            targetIntensity = Math.Clamp(targetIntensity, 0, 100);
            if (!_isAnimating && _appliedIntensity == targetIntensity)
                return;

            _fromIntensity = _appliedIntensity;
            _toIntensity = targetIntensity;
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

            // smoothstep で緩やかに補間
            progress = progress * progress * (3.0 - 2.0 * progress);

            var current = _fromIntensity + (_toIntensity - _fromIntensity) * progress;
            _appliedIntensity = (int)Math.Round(current);
            ApplyIntensity(_appliedIntensity);

            if (progress >= 1.0)
                StopAnimation();
        }

        private void StopAnimation()
        {
            _timer.Stop();
            _isAnimating = false;
        }

        private static void ApplyIntensity(int intensity)
        {
            if (intensity <= 0)
                GammaController.ResetGamma();
            else
                GammaController.SetGamma(intensity);
        }
    }
}
