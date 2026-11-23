using System.Runtime.InteropServices;

namespace TasollerPlus.Connections.WinUsb
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TasollerPlusWinUsbPipeInformation
    {
        public int PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }
}
