using System;
using System.Runtime.InteropServices;

namespace TasollerPlus
{
    internal static class TasollerPlusWinUsbPipeLayout
    {
        private const string WinUsbLibrary = "Winusb.dll";
        private const byte DefaultAlternateSetting = 0;
        private const byte TasollerPlusInterfaceNumber = 0;
        private const int BulkPipeType = 2;
        private const int InterruptPipeType = 3;

        public const byte InputPipeId = 0x84;
        public const byte LedOutputPipeId = 0x03;

        public static bool IsSupported(TasollerPlusWinUsbInterfaceHandle interfaceHandle)
        {
            if (!WinUsb_QueryInterfaceSettings(interfaceHandle, DefaultAlternateSetting, out var descriptor))
            {
                return false;
            }

            return IsSupported(descriptor.InterfaceNumber, descriptor.EndpointCount, index =>
            {
                if (WinUsb_QueryPipe(interfaceHandle, DefaultAlternateSetting, index, out var pipe))
                {
                    return (pipe.PipeId, pipe.PipeType);
                }

                return null;
            });
        }

        public static bool IsSupported(byte interfaceNumber, byte pipeCount, Func<byte, (byte PipeId, int PipeType)?> queryPipe)
        {
            if (interfaceNumber != TasollerPlusInterfaceNumber)
            {
                return false;
            }

            var hasInputPipe = false;
            var hasLedOutputPipe = false;
            for (var index = 0; index < pipeCount; index++)
            {
                if (queryPipe((byte)index) is { } pipe)
                {
                    hasInputPipe |= pipe.PipeId == InputPipeId && pipe.PipeType == InterruptPipeType;
                    hasLedOutputPipe |= pipe.PipeId == LedOutputPipeId && pipe.PipeType == BulkPipeType;
                }
            }

            return hasInputPipe && hasLedOutputPipe;
        }

        [DllImport(WinUsbLibrary, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinUsb_QueryInterfaceSettings(TasollerPlusWinUsbInterfaceHandle interfaceHandle, byte alternateSetting, out TasollerPlusWinUsbInterfaceDescriptor descriptor);

        [DllImport(WinUsbLibrary, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinUsb_QueryPipe(TasollerPlusWinUsbInterfaceHandle interfaceHandle, byte alternateSetting, byte pipeIndex, out TasollerPlusWinUsbPipeInformation pipe);
    }
}
