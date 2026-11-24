namespace TasollerPlus
{
    /// <summary>未読入力の取得件数と、キューの上限を超えて破棄された件数を表します。</summary>
    public readonly struct TasollerPlusInputReadResult
    {
        private readonly int _count;
        private readonly long _droppedCount;

        internal TasollerPlusInputReadResult(int count, long droppedCount)
        {
            _count = count;
            _droppedCount = droppedCount;
        }

        /// <summary>コピー先の配列の先頭から格納された入力フレームの件数を取得します。</summary>
        public int Count => _count;

        /// <summary>前回の未読入力取得以降に、キューの上限を超えて破棄された入力の件数を取得します。</summary>
        /// <remarks>接続後に初めて未読入力を取得したときは、接続開始以降の件数を示します。</remarks>
        public long DroppedCount => _droppedCount;
    }
}
