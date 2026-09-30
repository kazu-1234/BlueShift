using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace App1
{
    /// <summary>
    /// TimePicker をマウスホイールで分単位調節する。
    /// </summary>
    public static class TimePickerWheelHelper
    {
        private const double WheelNotch = 120.0;
        private static readonly TimeSpan Day = TimeSpan.FromDays(1);

        public static void HandlePointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not TimePicker picker || !picker.IsEnabled)
                return;

            var delta = e.GetCurrentPoint(picker).Properties.MouseWheelDelta;
            if (delta == 0)
                return;

            int steps = (int)Math.Round(delta / WheelNotch);
            if (steps == 0)
                steps = delta > 0 ? 1 : -1;

            int increment = picker.MinuteIncrement > 0 ? picker.MinuteIncrement : 1;
            long ticks = picker.Time.Ticks + TimeSpan.FromMinutes(steps * increment).Ticks;
            ticks %= Day.Ticks;
            if (ticks < 0)
                ticks += Day.Ticks;

            var newTime = TimeSpan.FromTicks(ticks);
            if (newTime == picker.Time)
                return;

            picker.Time = newTime;
            e.Handled = true;
        }
    }
}
