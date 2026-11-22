namespace TasollerPlus
{
    internal readonly struct TasollerPlusReadTransferResult
    {
        private readonly int _bytesRead;
        private readonly TasollerPlusReadTransferStatus _status;

        public TasollerPlusReadTransferResult(int bytesRead, TasollerPlusReadTransferStatus status = TasollerPlusReadTransferStatus.Succeeded)
        {
            _bytesRead = bytesRead;
            _status = status;
        }

        public int BytesRead => _bytesRead;

        public TasollerPlusReadTransferStatus Status => _status;
    }
}
