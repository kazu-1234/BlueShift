using System;
using System.Runtime.InteropServices;

namespace App1
{
    public static class GammaController
    {
        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

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

        /// <summary>
        /// 強制終了後も残りうるガンマ補正を標準の線形ランプへ戻す。
        /// </summary>
        public static void ResetGamma()
        {
            ApplyRamp(CreateIdentityRamp());
        }

        public static void SetGamma(int intensity)
        {
            ApplyRamp(CreateFilteredRamp(intensity));
        }

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

        private static RAMP CreateFilteredRamp(int intensity)
        {
            var ramp = CreateIdentityRamp();
            intensity = Math.Clamp(intensity, 0, 100);

            for (int i = 1; i < 256; i++)
            {
                double redValue = i * 255;
                double greenValue = i * 255 * (1.0 - intensity / 100.0 * 0.2);
                double blueValue = i * 255 * (1.0 - intensity / 100.0 * 0.8);

                ramp.Red[i] = (ushort)Math.Min(redValue, 65535);
                ramp.Green[i] = (ushort)Math.Min(greenValue, 65535);
                ramp.Blue[i] = (ushort)Math.Min(blueValue, 65535);
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
