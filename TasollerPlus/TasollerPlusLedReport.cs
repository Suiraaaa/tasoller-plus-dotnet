using System;

namespace TasollerPlus
{
    internal static class TasollerPlusLedReport
    {
        private const int HeaderLength = 3;
        private const int SliderColorCount = 31;
        private const int AirTowerColorCount = 3;
        private const int BytesPerColor = 3;
        private const int SliderOffset = HeaderLength;
        private const int LeftAirOffset = SliderOffset + SliderColorCount * BytesPerColor;
        private const int RightAirOffset = LeftAirOffset + AirTowerColorCount * BytesPerColor;
        private const int ReportLength = RightAirOffset + AirTowerColorCount * BytesPerColor;

        private static readonly byte[] Header = { 0x44, 0x4C, 0x02 };

        public static byte[] Encode(ReadOnlySpan<RgbColor> slider, ReadOnlySpan<RgbColor> leftAir, ReadOnlySpan<RgbColor> rightAir)
        {
            ValidateColorCount(slider.Length, SliderColorCount, nameof(slider));
            ValidateColorCount(leftAir.Length, AirTowerColorCount, nameof(leftAir));
            ValidateColorCount(rightAir.Length, AirTowerColorCount, nameof(rightAir));

            var report = new byte[ReportLength];
            var packet = report.AsSpan();
            Header.AsSpan().CopyTo(packet[..SliderOffset]);
            WriteColors(slider, packet[SliderOffset..LeftAirOffset]);
            WriteColors(leftAir, packet[LeftAirOffset..RightAirOffset]);
            WriteColors(rightAir, packet[RightAirOffset..]);
            return report;
        }

        private static void ValidateColorCount(int actualCount, int expectedCount, string parameterName)
        {
            if (actualCount != expectedCount)
            {
                throw new ArgumentException($"Expected {expectedCount} colors.", parameterName);
            }
        }

        private static void WriteColors(ReadOnlySpan<RgbColor> colors, Span<byte> destination)
        {
            var offset = 0;
            foreach (var color in colors)
            {
                destination[offset++] = color.R;
                destination[offset++] = color.G;
                destination[offset++] = color.B;
            }
        }
    }
}
