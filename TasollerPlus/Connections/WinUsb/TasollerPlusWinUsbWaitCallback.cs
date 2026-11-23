using System;
using System.Runtime.InteropServices;

namespace TasollerPlus.Connections.WinUsb
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate void TasollerPlusWinUsbWaitCallback(IntPtr instance, IntPtr context, IntPtr wait, uint waitResult);
}
