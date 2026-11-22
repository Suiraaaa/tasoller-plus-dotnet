using System;

namespace TasollerPlus
{
    public readonly struct TasollerPlusInputFrame
    {
        private readonly ReadOnlyMemory<byte> _sliderValues;

        internal TasollerPlusInputFrame(ReadOnlySpan<byte> sliderValues)
        {
            _sliderValues = sliderValues.ToArray();
        }

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
