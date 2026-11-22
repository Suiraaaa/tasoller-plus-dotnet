using System;

namespace TasollerPlus
{
    public readonly struct TasollerPlusInputFrame
    {
        private const int AirBeamCount = 6;
        private const byte BottomAirBeamMask = 0b0010_0000;

        private readonly ReadOnlyMemory<byte> _sliderValues;
        private readonly byte _digitalInputs;
        private readonly long _receivedTimestamp;
        private readonly long _sequenceNumber;

        internal TasollerPlusInputFrame(ReadOnlySpan<byte> sliderValues, byte digitalInputs, long receivedTimestamp, long sequenceNumber)
        {
            _digitalInputs = digitalInputs;
            _receivedTimestamp = receivedTimestamp;
            _sequenceNumber = sequenceNumber;

            _sliderValues = sliderValues.ToArray();
        }

        public long ReceivedTimestamp => _receivedTimestamp;

        public long SequenceNumber => _sequenceNumber;

        public byte GetSliderValue(int index)
        {
            if ((uint)index >= (uint)_sliderValues.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _sliderValues.Span[index];
        }

        public bool IsAirBlocked(int index)
        {
            if ((uint)index >= AirBeamCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var beamMask = BottomAirBeamMask >> index;
            return (_digitalInputs & beamMask) != 0;
        }
    }
}
