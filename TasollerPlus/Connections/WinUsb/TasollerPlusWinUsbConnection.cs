using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus.Connections.WinUsb
{
    internal sealed class TasollerPlusWinUsbConnection : ITasollerPlusConnection
    {
        private const uint MinimumInputByteCount = 0;
        private const int ErrorSuccess = 0;
        private const int ErrorDevNotExist = 55;
        private const int ErrorNoSuchDevice = 433;
        private const int ErrorIoPending = 997;
        private const int ErrorDeviceNotConnected = 1167;
        private const int ErrorDeviceRemoved = 1617;

        private readonly string _instanceId;
        private readonly ITasollerPlusWinUsbPipeOperations _pipes;
        private readonly Func<string, bool> _isDeviceAbsent;

        public TasollerPlusWinUsbConnection(string instanceId, ITasollerPlusWinUsbPipeOperations pipes, Func<string, bool>? isDeviceAbsent = null)
        {
            _instanceId = instanceId;
            _pipes = pipes;

            _isDeviceAbsent = isDeviceAbsent ?? TasollerPlusWinUsbDeviceList.IsDeviceAbsent;
        }

        public static TasollerPlusWinUsbConnection? Open(string instanceId)
        {
            foreach (var path in TasollerPlusWinUsbDeviceList.ReadPresentInterfacePaths(instanceId))
            {
                var pipes = TasollerPlusWinUsbPipeOperations.Open(path);
                if (pipes != null)
                {
                    return new TasollerPlusWinUsbConnection(instanceId, pipes);
                }
            }

            return null;
        }

        public async ValueTask<TasollerPlusReadTransferResult> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var transfer = await TransferAsync(buffer, TasollerPlusWinUsbPipeLayout.InputPipeId, MinimumInputByteCount, cancellationToken).ConfigureAwait(false);
            return new TasollerPlusReadTransferResult(transfer.ByteCount, transfer.Status);
        }

        public async ValueTask<TasollerPlusTransferStatus> WriteOutputAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            var transfer = await TransferAsync(report, TasollerPlusWinUsbPipeLayout.LedOutputPipeId, (uint)report.Length, cancellationToken).ConfigureAwait(false);
            return transfer.Status;
        }

        public void Dispose()
        {
            _pipes.Dispose();
        }

        private async ValueTask<(TasollerPlusTransferStatus Status, int ByteCount)> TransferAsync(ReadOnlyMemory<byte> buffer, byte pipeId, uint minimumByteCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bufferLength = (uint)buffer.Length;
            using var pinnedBuffer = buffer.Pin();
            using var completion = new TasollerPlusWinUsbCompletion();
            var requestAddress = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
            try
            {
                var overlapped = new NativeOverlapped { EventHandle = completion.EventHandle };
                Marshal.StructureToPtr(overlapped, requestAddress, fDeleteOld: false);
                using var registration = cancellationToken.Register(() => _pipes.CancelTransfer(requestAddress));
                cancellationToken.ThrowIfCancellationRequested();

                var submissionError = _pipes.StartTransfer(pipeId, GetBufferAddress(pinnedBuffer), bufferLength, requestAddress);
                if (submissionError == ErrorIoPending)
                {
                    // A cancellation callback may have run before WinUSB received the request.
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _pipes.CancelTransfer(requestAddress);
                    }

                    // CancelIoEx only requests cancellation. Keep the native memory alive until completion.
                    await completion.Completed.ConfigureAwait(false);
                }

                var (error, byteCount) = submissionError == ErrorSuccess || submissionError == ErrorIoPending
                    ? _pipes.GetTransferResult(requestAddress)
                    : (submissionError, default(uint));
                if (error != ErrorSuccess)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = ClassifyTransferFailure(error);
                    return (status, default);
                }

                var hasValidByteCount = byteCount >= minimumByteCount && byteCount <= bufferLength;
                return hasValidByteCount ? (TasollerPlusTransferStatus.Succeeded, (int)byteCount) : (TasollerPlusTransferStatus.Failed, default);
            }
            finally
            {
                Marshal.FreeHGlobal(requestAddress);
            }
        }

        private TasollerPlusTransferStatus ClassifyTransferFailure(int error)
        {
            var reportsRemoval = error == ErrorDevNotExist || error == ErrorNoSuchDevice || error == ErrorDeviceNotConnected || error == ErrorDeviceRemoved;
            var deviceAbsent = reportsRemoval || _isDeviceAbsent(_instanceId);
            return deviceAbsent ? TasollerPlusTransferStatus.DeviceRemoved : TasollerPlusTransferStatus.Failed;
        }

        private static unsafe IntPtr GetBufferAddress(MemoryHandle buffer)
        {
            return (IntPtr)buffer.Pointer;
        }
    }
}
