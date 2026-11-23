using System.Runtime.InteropServices;

namespace TasollerPlus
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TasollerPlusWinUsbInterfaceDescriptor
    {
        public byte Length;
        public byte DescriptorType;
        public byte InterfaceNumber;
        public byte AlternateSetting;
        public byte EndpointCount;
        public byte InterfaceClass;
        public byte InterfaceSubClass;
        public byte InterfaceProtocol;
        public byte StringDescriptorIndex;
    }
}
