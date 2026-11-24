using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TasollerPlus.Connections.WinUsb
{
    internal sealed class TasollerPlusWinUsbPipeOperations : ITasollerPlusWinUsbPipeOperations
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
        private const int ErrorSuccess = 0;

        private readonly SafeFileHandle _fileHandle;
        private readonly TasollerPlusWinUsbInterfaceHandle _interfaceHandle;

        private TasollerPlusWinUsbPipeOperations(SafeFileHandle fileHandle, TasollerPlusWinUsbInterfaceHandle interfaceHandle)
        {
            _fileHandle = fileHandle;
            _interfaceHandle = interfaceHandle;
        }

        public static TasollerPlusWinUsbPipeOperations? Open(string path)
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

                if (!TasollerPlusWinUsbPipeLayout.IsSupported(interfaceHandle))
                {
                    return null;
                }

                var pipes = new TasollerPlusWinUsbPipeOperations(fileHandle, interfaceHandle);
                ownershipTransferred = true;
                return pipes;
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

        public int StartTransfer(byte pipeId, IntPtr buffer, uint bufferLength, IntPtr overlapped)
        {
            var succeeded = pipeId == TasollerPlusWinUsbPipeLayout.InputPipeId
                ? WinUsb_ReadPipe(_interfaceHandle, pipeId, buffer, bufferLength, IntPtr.Zero, overlapped)
                : WinUsb_WritePipe(_interfaceHandle, pipeId, buffer, bufferLength, IntPtr.Zero, overlapped);
            return succeeded ? ErrorSuccess : Marshal.GetLastWin32Error();
        }

        public (int Error, uint ByteCount) GetTransferResult(IntPtr overlapped)
        {
            var succeeded = WinUsb_GetOverlappedResult(_interfaceHandle, overlapped, out var byteCount, wait: false);
            return (succeeded ? ErrorSuccess : Marshal.GetLastWin32Error(), byteCount);
        }

        public void CancelTransfer(IntPtr overlapped)
        {
            CancelIoEx(_fileHandle, overlapped);
        }

        public void Dispose()
        {
            _interfaceHandle.Dispose();
            _fileHandle.Dispose();
        }

        [DllImport(KernelLibrary, EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr templateFile);

        [DllImport(WinUsbLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinUsb_Initialize(SafeFileHandle fileHandle, out TasollerPlusWinUsbInterfaceHandle interfaceHandle);

        [DllImport(WinUsbLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinUsb_ReadPipe(TasollerPlusWinUsbInterfaceHandle interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, IntPtr bytesTransferred, IntPtr overlapped);

        [DllImport(WinUsbLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinUsb_WritePipe(TasollerPlusWinUsbInterfaceHandle interfaceHandle, byte pipeId, IntPtr buffer, uint bufferLength, IntPtr bytesTransferred, IntPtr overlapped);

        [DllImport(WinUsbLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinUsb_GetOverlappedResult(TasollerPlusWinUsbInterfaceHandle interfaceHandle, IntPtr overlapped, out uint bytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool wait);

        [DllImport(KernelLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CancelIoEx(SafeFileHandle fileHandle, IntPtr overlapped);
    }
}
