using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace App1
{
    /// <summary>
    /// スライダーをマウスホイールで調節する（ガンマプレビューは行わない）。
    /// </summary>
    public static class SliderWheelHelper
    {
        private const double WheelNotch = 120.0;

        public static void HandlePointerWheelChanged(object sender, PointerRoutedEventArgs e, double step = 1.0)
        {
            if (sender is not Slider slider || !slider.IsEnabled)
                return;

            var delta = e.GetCurrentPoint(slider).Properties.MouseWheelDelta;
            if (delta == 0)
                return;

            int steps = (int)Math.Round(delta / WheelNotch);
            if (steps == 0)
                steps = delta > 0 ? 1 : -1;

            var newValue = Math.Clamp(slider.Value + steps * step, slider.Minimum, slider.Maximum);
            if (Math.Abs(newValue - slider.Value) < 0.001)
                return;

            slider.Value = newValue;
            e.Handled = true;
        }
    }
}
