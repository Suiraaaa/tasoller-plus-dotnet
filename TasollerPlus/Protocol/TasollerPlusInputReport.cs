using System;

namespace TasollerPlus.Protocol
{
    internal static class TasollerPlusInputReport
    {
        private const int DigitalInputsOffset = 3;
        private const int SliderValuesOffset = DigitalInputsOffset + sizeof(byte);
        private const int SliderValueCount = 32;
        public const int Length = SliderValuesOffset + SliderValueCount;

        private static readonly byte[] Header = { 0x44, 0x42, 0x54 };

        public static TasollerPlusInputFrame? Decode(ReadOnlySpan<byte> report, long receivedTimestamp, long sequenceNumber)
        {
            if (report.Length != Length || !report.StartsWith(Header))
            {
                return null;
            }

            var digitalInputs = report[DigitalInputsOffset];
            var sliderValues = report.Slice(SliderValuesOffset, SliderValueCount);
            return new TasollerPlusInputFrame(sliderValues, digitalInputs, receivedTimestamp, sequenceNumber);
        }
    }
}
