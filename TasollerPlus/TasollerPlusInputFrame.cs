using System;

namespace TasollerPlus
{
    public readonly struct TasollerPlusInputFrame
    {
        private const int BottomAirBeamMask = 0b0000_0001;
        internal const int AirBeamCount = 6;

        private readonly ReadOnlyMemory<byte> _sliderValues;
        private readonly byte _blockedAirBeamMask;
        private readonly bool _isFn1Pressed;
        private readonly bool _isFn2Pressed;
        private readonly long _receivedTimestamp;
        private readonly long _sequenceNumber;

        internal TasollerPlusInputFrame(ReadOnlySpan<byte> sliderValues, bool isFn1Pressed, bool isFn2Pressed, byte blockedAirBeamMask, long receivedTimestamp, long sequenceNumber)
        {
            _isFn1Pressed = isFn1Pressed;
            _isFn2Pressed = isFn2Pressed;
            _blockedAirBeamMask = blockedAirBeamMask;
            _receivedTimestamp = receivedTimestamp;
            _sequenceNumber = sequenceNumber;

            _sliderValues = sliderValues.ToArray();
        }

        public long ReceivedTimestamp => _receivedTimestamp;

        public long SequenceNumber => _sequenceNumber;

        public bool IsFn1Pressed => _isFn1Pressed;

        public bool IsFn2Pressed => _isFn2Pressed;

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

            var beamMask = BottomAirBeamMask << index;
            return (_blockedAirBeamMask & beamMask) != 0;
        }
    }
}
