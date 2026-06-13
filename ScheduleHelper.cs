using System;
using System.Collections.Generic;
using System.Linq;

namespace App1
{
    /// <summary>
    /// スケジュールの時刻判定と次回切替時刻の計算。
    /// 終了18:00と開始18:00は排他的境界（終了&lt;18:00、開始&gt;=18:00）でスムーズに切替。
    /// </summary>
    public static class ScheduleHelper
    {
        /// <summary>
        /// 現在有効なパターンを返す。
        /// 終了時刻あり: [開始, 終了)。終了なし: [開始, 次パターンの開始)（日跨ぎ対応）。
        /// 該当なしのときは null（ブルーライト無効）。
        /// </summary>
        public static Pattern? ResolveActivePattern(IEnumerable<Pattern> patterns, DateTime now)
        {
            var sorted = patterns.OrderBy(p => p.Time).ToList();
            if (sorted.Count == 0)
                return null;

            var nowStr = now.ToString("HH:mm");
            var matches = new List<Pattern>();

            for (int i = 0; i < sorted.Count; i++)
            {
                var pattern = sorted[i];
                string end = pattern.HasEndTime
                    ? pattern.EndTime
                    : sorted[(i + 1) % sorted.Count].Time;

                if (IsTimeInRange(pattern.Time, end, nowStr))
                    matches.Add(pattern);
            }

            return matches
                .OrderByDescending(p => p.Time)
                .FirstOrDefault();
        }

        public static bool IsTimeInRange(string start, string end, string now)
        {
            if (start == end) return true;

            if (string.Compare(start, end, StringComparison.Ordinal) < 0)
            {
                return string.Compare(now, start, StringComparison.Ordinal) >= 0
                    && string.Compare(now, end, StringComparison.Ordinal) < 0;
            }

            // 日をまたぐ範囲（例: 22:00 〜 06:00）
            return string.Compare(now, start, StringComparison.Ordinal) >= 0
                || string.Compare(now, end, StringComparison.Ordinal) < 0;
        }

        /// <summary>
        /// 次のスケジュール切替までの待機時間。パターンが無い場合は null。
        /// </summary>
        public static TimeSpan? GetDelayUntilNextTransition(IEnumerable<Pattern> patterns, DateTime now)
        {
            var list = patterns.ToList();
            if (list.Count == 0) return null;

            var delays = new List<TimeSpan>();
            foreach (var pattern in list)
            {
                AddTransitionDelay(delays, pattern.Time, now);
                if (pattern.HasEndTime)
                    AddTransitionDelay(delays, pattern.EndTime, now);
            }

            if (delays.Count == 0) return TimeSpan.FromHours(1);

            var min = delays.Min();
            if (min < TimeSpan.FromSeconds(1))
                return TimeSpan.FromSeconds(1);

            return min;
        }

        private static void AddTransitionDelay(List<TimeSpan> delays, string timeStr, DateTime now)
        {
            if (!TimeSpan.TryParse(timeStr, out var timeOfDay)) return;

            var target = now.Date + timeOfDay;
            if (target <= now)
                target = target.AddDays(1);

            delays.Add(target - now);
        }
    }
}
