namespace TasollerPlus
{
    public readonly struct RgbColor
    {
        private readonly byte _red;
        private readonly byte _green;
        private readonly byte _blue;

        public RgbColor(byte red, byte green, byte blue)
        {
            _red = red;
            _green = green;
            _blue = blue;
        }

        public byte R => _red;

        public byte G => _green;

        public byte B => _blue;
    }
}
