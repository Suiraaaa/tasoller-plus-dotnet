using System;

namespace TasollerPlus
{
    internal static class TasollerPlusInputReport
    {
        private const int SliderValuesOffset = 4;
        private const int SliderValueCount = 32;
        public const int Length = SliderValuesOffset + SliderValueCount;

        private static readonly byte[] Header = { 0x44, 0x42, 0x54 };

        public static TasollerPlusInputFrame? Decode(ReadOnlySpan<byte> report)
        {
            if (report.Length != Length || !report.StartsWith(Header))
            {
                return null;
            }

            return new TasollerPlusInputFrame(report.Slice(SliderValuesOffset, SliderValueCount));
        }
    }
}
