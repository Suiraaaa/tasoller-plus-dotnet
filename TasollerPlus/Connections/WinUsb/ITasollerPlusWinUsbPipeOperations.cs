using System;

namespace TasollerPlus.Connections.WinUsb
{
    internal interface ITasollerPlusWinUsbPipeOperations : IDisposable
    {
        int StartTransfer(byte pipeId, IntPtr buffer, uint bufferLength, IntPtr overlapped);

        // Call after immediate success or after the completion event is signaled.
        (int Error, uint ByteCount) GetTransferResult(IntPtr overlapped);

        // The caller keeps the transfer resources alive until the completion event is signaled.
        void CancelTransfer(IntPtr overlapped);
    }
}
