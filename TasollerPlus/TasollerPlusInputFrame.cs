using System;

namespace TasollerPlus
{
    public readonly struct TasollerPlusInputFrame
    {
        private readonly ReadOnlyMemory<byte> _sliderValues;
        private readonly long _receivedTimestamp;
        private readonly long _sequenceNumber;

        internal TasollerPlusInputFrame(ReadOnlySpan<byte> sliderValues, long receivedTimestamp, long sequenceNumber)
        {
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
    }
}
