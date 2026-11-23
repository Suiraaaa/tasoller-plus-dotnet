using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace TasollerPlus
{
    internal sealed class TasollerPlusWinUsbConnection : ITasollerPlusConnection
    {
        private const string WinUsbLibrary = "Winusb.dll";
        private const string KernelLibrary = "Kernel32.dll";
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint ShareRead = 0x00000001;
        private const uint ShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileFlagOverlapped = 0x40000000;
        private const uint FileAttributeNormal = 0x00000080;
        private const int ErrorIoPending = 997;
        private const int ErrorDeviceNotConnected = 1167;
        private const byte InputPipeId = 0x84;
        private const byte LedOutputPipeId = 0x03;

        private readonly SafeFileHandle _fileHandle;
        private readonly TasollerPlusWinUsbInterfaceHandle _interfaceHandle;

        private TasollerPlusWinUsbConnection(SafeFileHandle fileHandle, TasollerPlusWinUsbInterfaceHandle interfaceHandle)
        {
            _fileHandle = fileHandle;
            _interfaceHandle = interfaceHandle;
        }

        public static TasollerPlusWinUsbConnection? Open(string instanceId)
        {
            foreach (var path in TasollerPlusWinUsbDeviceList.ReadPresentInterfacePaths(instanceId))
            {
                var connection = OpenPath(path);
                if (connection != null)
                {
                    return connection;
                }
            }

            return null;
        }

        public ValueTask<TasollerPlusReadTransferResult> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            using var pinnedBuffer = buffer.Pin();
            var transfer = Transfer(pinnedBuffer, buffer.Length, InputPipeId, cancellationToken);
            return new ValueTask<TasollerPlusReadTransferResult>(new TasollerPlusReadTransferResult(transfer.ByteCount, transfer.Status));
        }

        public ValueTask<TasollerPlusTransferStatus> WriteOutputAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            using var pinnedBuffer = report.Pin();
            var transfer = Transfer(pinnedBuffer, report.Length, LedOutputPipeId, cancellationToken);
            var status = transfer.Status == TasollerPlusTransferStatus.Succeeded && transfer.ByteCount != report.Length ? TasollerPlusTransferStatus.Failed : transfer.Status;
            return new ValueTask<TasollerPlusTransferStatus>(status);
        }

        public void Dispose()
        {
            _interfaceHandle.Dispose();
            _fileHandle.Dispose();
        }

        private static TasollerPlusWinUsbConnection? OpenPath(string path)
        {
            var fileHandle = CreateFile(path, GenericRead | GenericWrite, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped | FileAttributeNormal, IntPtr.Zero);
            TasollerPlusWinUsbInterfaceHandle? interfaceHandle = null;
            var ownershipTransferred = false;
            try
            {
                if (fileHandle.IsInvalid || !WinUsb_Initialize(fileHandle, out interfaceHandle) || interfaceHandle.IsInvalid)
                {
                    return null;
                }

                var connection = new TasollerPlusWinUsbConnection(fileHandle, interfaceHandle);
                ownershipTransferred = true;
                return connection;
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    interfaceHandle?.Dispose();
                    fileHandle.Dispose();
                }
            }
        }

        private unsafe (TasollerPlusTransferStatus Status, int ByteCount) Transfer(MemoryHandle pinnedBuffer, int length, byte pipeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var completed = new EventWaitHandle(initialState: false, EventResetMode.ManualReset);
            var overlapped = new NativeOverlapped { EventHandle = completed.SafeWaitHandle.DangerousGetHandle() };
            var byteCount = 0u;
            var succeeded = pipeId == InputPipeId
                ? WinUsb_ReadPipe(_interfaceHandle, pipeId, pinnedBuffer.Pointer, (uint)length, &byteCount, &overlapped)
                : WinUsb_WritePipe(_interfaceHandle, pipeId, pinnedBuffer.Pointer, (uint)length, &byteCount, &overlapped);

            if (!succeeded)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorIoPending)
                {
                    var requestAddress = (IntPtr)(&overlapped);
                    using var registration = cancellationToken.Register(() => CancelIoEx(_fileHandle, requestAddress));
                    // Even after cancellation, the request owns the pinned buffer and event until completion.
                    succeeded = WinUsb_GetOverlappedResult(_interfaceHandle, &overlapped, &byteCount, wait: true);
                    error = Marshal.GetLastWin32Error();
                }

                if (!succeeded)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = error == ErrorDeviceNotConnected ? TasollerPlusTransferStatus.DeviceRemoved : TasollerPlusTransferStatus.Failed;
                    return (status, 0);
                }
            }

            return byteCount <= (uint)length ? (TasollerPlusTransferStatus.Succeeded, (int)byteCount) : (TasollerPlusTransferStatus.Failed, 0);
        }

        [DllImport(KernelLibrary, EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr templateFile);

        [DllImport(WinUsbLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinUsb_Initialize(SafeFileHandle fileHandle, out TasollerPlusWinUsbInterfaceHandle interfaceHandle);

        [DllImport(WinUsbLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool WinUsb_ReadPipe(TasollerPlusWinUsbInterfaceHandle interfaceHandle, byte pipeId, void* buffer, uint bufferLength, uint* bytesTransferred, NativeOverlapped* overlapped);

        [DllImport(WinUsbLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool WinUsb_WritePipe(TasollerPlusWinUsbInterfaceHandle interfaceHandle, byte pipeId, void* buffer, uint bufferLength, uint* bytesTransferred, NativeOverlapped* overlapped);

        [DllImport(WinUsbLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool WinUsb_GetOverlappedResult(TasollerPlusWinUsbInterfaceHandle interfaceHandle, NativeOverlapped* overlapped, uint* bytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport(KernelLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CancelIoEx(SafeFileHandle fileHandle, IntPtr overlapped);
    }
}
