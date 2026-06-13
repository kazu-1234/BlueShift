using System;

namespace App1
{
    /// <summary>
    /// 色温度（K）をガンマランプ用の RGB 乗数へ変換する。
    /// 6500K を基準（1,1,1）とし、チャンネル比の変化で暖色/寒色を表現する。
    /// </summary>
    internal static class ColorTemperatureHelper
    {
        /// <summary>基準色温度に対する RGB 乗数を返す。</summary>
        public static (double Red, double Green, double Blue) GetMultipliersRelativeToDefault(int kelvin)
        {
            kelvin = Math.Clamp(kelvin, GammaSettings.MinColorTemperatureKelvin, GammaSettings.MaxColorTemperatureKelvin);

            var (red, green, blue) = KelvinToLinearRgb(kelvin);
            var (refRed, refGreen, refBlue) = KelvinToLinearRgb(GammaSettings.DefaultColorTemperatureKelvin);

            return (
                red / refRed,
                green / refGreen,
                blue / refBlue);
        }

        /// <summary>Tanner Helland 近似による黒体軌跡 RGB（0〜1）。</summary>
        private static (double Red, double Green, double Blue) KelvinToLinearRgb(int kelvin)
        {
            double temp = Math.Clamp(kelvin, 1000, 40000) / 100.0;
            double red;
            double green;
            double blue;

            if (temp <= 66)
            {
                red = 255;
                green = 99.4708025861 * Math.Log(temp) - 161.1195681661;
                green = Math.Clamp(green, 0, 255);

                if (temp <= 19)
                    blue = 0;
                else
                {
                    blue = 138.5177312231 * Math.Log(temp - 10) - 305.0447927307;
                    blue = Math.Clamp(blue, 0, 255);
                }
            }
            else
            {
                red = 329.698727446 * Math.Pow(temp - 60, -0.1332047592);
                green = 288.1221695283 * Math.Pow(temp - 60, -0.0755148492);
                blue = 255;
                red = Math.Clamp(red, 0, 255);
                green = Math.Clamp(green, 0, 255);
            }

            return (red / 255.0, green / 255.0, blue / 255.0);
        }
    }
}
