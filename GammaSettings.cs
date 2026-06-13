using System;

namespace App1
{
    /// <summary>ガンマ適用パラメータ（強度と色温度）。</summary>
    public readonly struct GammaSettings : IEquatable<GammaSettings>
    {
        public const int MinColorTemperatureKelvin = 2700;
        public const int MaxColorTemperatureKelvin = 6500;
        public const int DefaultColorTemperatureKelvin = 6500;

        public int Intensity { get; init; }
        public int ColorTemperatureKelvin { get; init; }

        public static GammaSettings Off => new()
        {
            Intensity = 0,
            ColorTemperatureKelvin = DefaultColorTemperatureKelvin
        };

        public static GammaSettings FromPattern(Pattern pattern) => new()
        {
            Intensity = pattern.Intensity,
            ColorTemperatureKelvin = pattern.ColorTemperatureKelvin
        };

        public GammaSettings Clamp()
        {
            return new GammaSettings
            {
                Intensity = Math.Clamp(Intensity, 0, 100),
                ColorTemperatureKelvin = Math.Clamp(
                    ColorTemperatureKelvin,
                    MinColorTemperatureKelvin,
                    MaxColorTemperatureKelvin)
            };
        }

        public bool Equals(GammaSettings other)
        {
            return Intensity == other.Intensity
                && ColorTemperatureKelvin == other.ColorTemperatureKelvin;
        }

        public override bool Equals(object? obj) =>
            obj is GammaSettings other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Intensity, ColorTemperatureKelvin);
    }
}
