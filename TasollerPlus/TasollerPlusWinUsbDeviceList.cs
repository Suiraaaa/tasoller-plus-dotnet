using System;
using System.Runtime.InteropServices;

namespace TasollerPlus
{
    internal static class TasollerPlusWinUsbDeviceList
    {
        private const string ConfigurationManagerLibrary = "CfgMgr32.dll";
        private const string WinUsbServiceName = "WinUSB";
        private const uint FilterService = 0x00000002;
        private const uint FilterPresent = 0x00000100;
        private const uint DoNotGenerateDeviceNode = 0x10000040;
        private const uint DeviceListFlags = FilterService | FilterPresent | DoNotGenerateDeviceNode;
        private const uint ConfigurationSuccess = 0x00000000;
        private const uint ConfigurationBufferTooSmall = 0x0000001A;
        private const int MaxEnumerationAttempts = 3;

        public static string[]? ReadPresentDeviceInstanceIds()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            for (var attempt = 0; attempt < MaxEnumerationAttempts; attempt++)
            {
                var sizeResult = GetDeviceIdListSize(out var characterCount, WinUsbServiceName, DeviceListFlags);
                if (sizeResult != ConfigurationSuccess)
                {
                    return null;
                }

                var buffer = new char[characterCount];
                var listResult = GetDeviceIdList(WinUsbServiceName, buffer, characterCount, DeviceListFlags);
                if (listResult == ConfigurationSuccess)
                {
                    return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                }

                // A newly attached device can grow the list after the size query.
                if (listResult != ConfigurationBufferTooSmall)
                {
                    return null;
                }
            }

            return null;
        }

        [DllImport(ConfigurationManagerLibrary, EntryPoint = "CM_Get_Device_ID_List_SizeW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDeviceIdListSize(out uint characterCount, string filter, uint flags);

        [DllImport(ConfigurationManagerLibrary, EntryPoint = "CM_Get_Device_ID_ListW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDeviceIdList(string filter, [Out] char[] buffer, uint characterCount, uint flags);
    }
}
