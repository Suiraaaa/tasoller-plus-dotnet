namespace TasollerPlus
{
    /// <summary>赤、緑、青の3成分でLEDの色を表します。</summary>
    public readonly struct RgbColor
    {
        private readonly byte _red;
        private readonly byte _green;
        private readonly byte _blue;

        /// <summary>指定されたRGB成分で色を作成します。</summary>
        /// <param name="red">赤の成分。範囲は0〜255です。</param>
        /// <param name="green">緑の成分。範囲は0〜255です。</param>
        /// <param name="blue">青の成分。範囲は0〜255です。</param>
        public RgbColor(byte red, byte green, byte blue)
        {
            _red = red;
            _green = green;
            _blue = blue;
        }

        /// <summary>赤の成分を取得します。</summary>
        public byte R => _red;

        /// <summary>緑の成分を取得します。</summary>
        public byte G => _green;

        /// <summary>青の成分を取得します。</summary>
        public byte B => _blue;
    }
}
