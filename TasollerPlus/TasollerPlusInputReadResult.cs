namespace TasollerPlus
{
    public readonly struct TasollerPlusInputReadResult
    {
        public int Count { get; }

        internal TasollerPlusInputReadResult(int count)
        {
            Count = count;
        }
    }
}
