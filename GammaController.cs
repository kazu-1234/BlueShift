using System;
using System.Runtime.InteropServices;

namespace App1
{
    public static class GammaController
    {
        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

        [DllImport("gdi32.dll")]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

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
        public static void ResetGamma()
        {
            ApplyRamp(CreateIdentityRamp());
        }

        public static void SetGamma(int intensity)
        {
            SetGamma(new GammaSettings
            {
                Intensity = intensity,
                ColorTemperatureKelvin = GammaSettings.DefaultColorTemperatureKelvin
            });
        }

        public static void SetGamma(GammaSettings settings)
        {
            settings = settings.Clamp();

            bool hasIntensity = settings.Intensity > 0;
            bool hasTemperatureShift =
                settings.ColorTemperatureKelvin < GammaSettings.DefaultColorTemperatureKelvin;

            if (!hasIntensity && !hasTemperatureShift)
            {
                ResetGamma();
                return;
            }

            ApplyRamp(CreateFilteredRamp(settings));
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
            ramp = default;
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
                return false;

            try
            {
                ramp = new RAMP
                {
                    Red = new ushort[256],
                    Green = new ushort[256],
                    Blue = new ushort[256]
                };

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

        private static RAMP CreateIdentityRamp()
        {
            var ramp = new RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };

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

        private static void ApplyRamp(RAMP ramp)
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
                return;

            try
            {
                SetDeviceGammaRamp(dc, ref ramp);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }
    }
}
