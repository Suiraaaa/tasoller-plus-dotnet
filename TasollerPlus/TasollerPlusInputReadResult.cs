namespace TasollerPlus
{
    public readonly struct TasollerPlusInputReadResult
    {
        private readonly int _count;

        internal TasollerPlusInputReadResult(int count)
        {
            _count = count;
        }

        public int Count => _count;
    }
}
