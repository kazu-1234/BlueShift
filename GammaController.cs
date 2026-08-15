using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace App1
{
    public static class GammaController
    {
        /// <summary>復帰直後などドライバ未準備時の短バックオフ再試行。</summary>
        private static readonly int[] ApplyRetryDelaysMs = { 0, 50, 150, 400 };

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

        [DllImport("gdi32.dll")]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDC(string? lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct RAMP
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public ushort[] Blue;
        }

        /// <summary>強制終了後も残りうるガンマ補正を標準の線形ランプへ戻す。</summary>
        public static bool ResetGamma()
        {
            return ApplyRamp(CreateIdentityRamp());
        }

        public static bool SetGamma(int intensity)
        {
            return SetGamma(new GammaSettings
            {
                Intensity = intensity,
                ColorTemperatureKelvin = GammaSettings.DefaultColorTemperatureKelvin
            });
        }

        public static bool SetGamma(GammaSettings settings)
        {
            settings = settings.Clamp();

            bool hasIntensity = settings.Intensity > 0;
            bool hasTemperatureShift =
                settings.ColorTemperatureKelvin < GammaSettings.DefaultColorTemperatureKelvin;

            if (!hasIntensity && !hasTemperatureShift)
                return ResetGamma();

            return ApplyRamp(CreateFilteredRamp(settings));
        }

        /// <summary>OS や他アプリによりガンマが戻されていないかを概算で判定する。</summary>
        public static bool IsLikelyApplied(GammaSettings settings)
        {
            settings = settings.Clamp();

            bool shouldApply = settings.Intensity > 0
                || settings.ColorTemperatureKelvin < GammaSettings.DefaultColorTemperatureKelvin;

            if (!TryGetCurrentRamp(out var actual))
                return false;

            if (!shouldApply)
                return IsNearIdentity(actual);

            var expected = CreateFilteredRamp(settings);
            return RampsAreSimilar(expected, actual);
        }

        private static bool TryGetCurrentRamp(out RAMP ramp)
        {
            ramp = CreateEmptyRamp();
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
                return false;

            try
            {
                return GetDeviceGammaRamp(dc, ref ramp);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private static bool IsNearIdentity(RAMP ramp)
        {
            foreach (int index in SampleIndices)
            {
                ushort expected = (ushort)Math.Min(index * 257, 65535);
                if (Math.Abs(ramp.Red[index] - expected) > RampTolerance)
                    return false;
                if (Math.Abs(ramp.Green[index] - expected) > RampTolerance)
                    return false;
                if (Math.Abs(ramp.Blue[index] - expected) > RampTolerance)
                    return false;
            }

            return true;
        }

        private static bool RampsAreSimilar(RAMP expected, RAMP actual)
        {
            foreach (int index in SampleIndices)
            {
                if (Math.Abs(expected.Red[index] - actual.Red[index]) > RampTolerance)
                    return false;
                if (Math.Abs(expected.Green[index] - actual.Green[index]) > RampTolerance)
                    return false;
                if (Math.Abs(expected.Blue[index] - actual.Blue[index]) > RampTolerance)
                    return false;
            }

            return true;
        }

        private static readonly int[] SampleIndices = { 64, 128, 192, 255 };
        private const int RampTolerance = 80;

        private static RAMP CreateEmptyRamp()
        {
            return new RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };
        }

        private static RAMP CreateIdentityRamp()
        {
            var ramp = CreateEmptyRamp();

            for (int i = 0; i < 256; i++)
            {
                ushort value = (ushort)Math.Min(i * 257, 65535);
                ramp.Red[i] = value;
                ramp.Green[i] = value;
                ramp.Blue[i] = value;
            }

            return ramp;
        }

        private static RAMP CreateFilteredRamp(GammaSettings settings)
        {
            var ramp = CreateIdentityRamp();
            int intensity = settings.Intensity;
            var (tempRed, tempGreen, tempBlue) =
                ColorTemperatureHelper.GetMultipliersRelativeToDefault(settings.ColorTemperatureKelvin);

            double blueIntensityFactor = 1.0 - intensity / 100.0 * 0.8;
            double greenIntensityFactor = 1.0 - intensity / 100.0 * 0.2;

            for (int i = 1; i < 256; i++)
            {
                double linear = i * 257.0;

                double redValue = linear * tempRed;
                double greenValue = linear * greenIntensityFactor * tempGreen;
                double blueValue = linear * blueIntensityFactor * tempBlue;

                // クリップでチャンネル比が崩れないよう、ピークで正規化する
                double peak = Math.Max(redValue, Math.Max(greenValue, blueValue));
                if (peak > 65535)
                {
                    double scale = 65535.0 / peak;
                    redValue *= scale;
                    greenValue *= scale;
                    blueValue *= scale;
                }

                ramp.Red[i] = (ushort)Math.Round(Math.Clamp(redValue, 0, 65535));
                ramp.Green[i] = (ushort)Math.Round(Math.Clamp(greenValue, 0, 65535));
                ramp.Blue[i] = (ushort)Math.Round(Math.Clamp(blueValue, 0, 65535));
            }

            return ramp;
        }

        private static bool ApplyRamp(RAMP ramp)
        {
            foreach (int delayMs in ApplyRetryDelaysMs)
            {
                if (delayMs > 0)
                    Thread.Sleep(delayMs);

                if (TryApplyRampToDisplays(ramp))
                    return true;
            }

            return false;
        }

        private static bool TryApplyRampToDisplays(RAMP ramp)
        {
            var devices = new List<string>();
            MonitorEnumProc proc = (hMonitor, _, _, _) =>
            {
                var info = new MONITORINFOEX
                {
                    cbSize = Marshal.SizeOf<MONITORINFOEX>()
                };
                if (GetMonitorInfo(hMonitor, ref info) && !string.IsNullOrEmpty(info.szDevice))
                    devices.Add(info.szDevice);
                return true;
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);

            bool anyVerified = false;
            foreach (string device in devices)
            {
                if (TryApplyRampToDevice(device, ramp))
                    anyVerified = true;
            }

            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc != IntPtr.Zero)
            {
                try
                {
                    if (TrySetAndVerify(screenDc, ramp))
                        anyVerified = true;
                }
                finally
                {
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }

            return anyVerified;
        }

        private static bool TryApplyRampToDevice(string device, RAMP ramp)
        {
            IntPtr dc = CreateDC(device, device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
                dc = CreateDC("DISPLAY", device, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
                return false;

            try
            {
                return TrySetAndVerify(dc, ramp);
            }
            finally
            {
                DeleteDC(dc);
            }
        }

        private static bool TrySetAndVerify(IntPtr dc, RAMP ramp)
        {
            if (!SetDeviceGammaRamp(dc, ref ramp))
                return false;

            var actual = CreateEmptyRamp();
            if (!GetDeviceGammaRamp(dc, ref actual))
                return false;

            return RampsAreSimilar(ramp, actual);
        }
    }
}
