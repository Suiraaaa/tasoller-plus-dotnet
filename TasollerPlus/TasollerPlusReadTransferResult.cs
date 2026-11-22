namespace TasollerPlus
{
    internal readonly struct TasollerPlusReadTransferResult
    {
        private readonly int _bytesRead;
        private readonly TasollerPlusTransferStatus _status;

        public TasollerPlusReadTransferResult(int bytesRead, TasollerPlusTransferStatus status = TasollerPlusTransferStatus.Succeeded)
        {
            _bytesRead = bytesRead;
            _status = status;
        }

        public int BytesRead => _bytesRead;

        public TasollerPlusTransferStatus Status => _status;
    }
}
