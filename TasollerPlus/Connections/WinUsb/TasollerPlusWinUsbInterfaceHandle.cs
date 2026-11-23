using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TasollerPlus.Connections.WinUsb
{
    internal sealed class TasollerPlusWinUsbInterfaceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public TasollerPlusWinUsbInterfaceHandle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle()
        {
            return WinUsb_Free(handle);
        }

        [DllImport("Winusb.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WinUsb_Free(IntPtr interfaceHandle);
    }
}
