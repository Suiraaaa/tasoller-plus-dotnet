namespace TasollerPlus
{
    internal readonly struct TasollerPlusReadTransferResult
    {
        private readonly int _bytesRead;
        private readonly bool _isDeviceRemoved;

        public TasollerPlusReadTransferResult(int bytesRead, bool isDeviceRemoved = false)
        {
            _bytesRead = bytesRead;
            _isDeviceRemoved = isDeviceRemoved;
        }

        public int BytesRead => _bytesRead;

        public bool IsDeviceRemoved => _isDeviceRemoved;
    }
}
