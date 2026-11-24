using System;

namespace TasollerPlus.Protocol
{
    internal static class TasollerPlusInputReport
    {
        private const int DigitalInputsOffset = 3;
        private const int SliderValuesOffset = DigitalInputsOffset + sizeof(byte);
        private const int SliderValueCount = 32;
        private const byte Fn1ButtonMask = 0b1000_0000;
        private const byte Fn2ButtonMask = 0b0100_0000;
        private const int BottomAirBeamReportBitIndex = 5;
        private const int SingleBitMask = 0b0000_0001;
        public const int Length = SliderValuesOffset + SliderValueCount;

        private static readonly byte[] Header = { 0x44, 0x42, 0x54 };

        public static TasollerPlusInputFrame? Decode(ReadOnlySpan<byte> report, long receivedTimestamp, long sequenceNumber)
        {
            if (report.Length != Length || !report.StartsWith(Header))
            {
                return null;
            }

            var digitalInputs = report[DigitalInputsOffset];
            var isFn1Pressed = (digitalInputs & Fn1ButtonMask) != 0;
            var isFn2Pressed = (digitalInputs & Fn2ButtonMask) != 0;
            var blockedAirBeamMask = DecodeBlockedAirBeamMask(digitalInputs);
            var sliderValues = report.Slice(SliderValuesOffset, SliderValueCount);
            return new TasollerPlusInputFrame(sliderValues, isFn1Pressed, isFn2Pressed, blockedAirBeamMask, receivedTimestamp, sequenceNumber);
        }

        private static byte DecodeBlockedAirBeamMask(byte digitalInputs)
        {
            var blockedAirBeamMask = 0;
            for (var beamIndex = 0; beamIndex < TasollerPlusInputFrame.AirBeamCount; beamIndex++)
            {
                var reportBitIndex = BottomAirBeamReportBitIndex - beamIndex;
                var blockedBit = (digitalInputs >> reportBitIndex) & SingleBitMask;
                blockedAirBeamMask |= blockedBit << beamIndex;
            }

            return (byte)blockedAirBeamMask;
        }
    }
}
