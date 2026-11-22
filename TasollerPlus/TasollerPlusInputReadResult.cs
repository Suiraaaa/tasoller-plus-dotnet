namespace TasollerPlus
{
    public readonly struct TasollerPlusInputReadResult
    {
        private readonly int _count;
        private readonly long _droppedCount;

        internal TasollerPlusInputReadResult(int count, long droppedCount)
        {
            _count = count;
            _droppedCount = droppedCount;
        }

        public int Count => _count;
        public long DroppedCount => _droppedCount;
    }
}
