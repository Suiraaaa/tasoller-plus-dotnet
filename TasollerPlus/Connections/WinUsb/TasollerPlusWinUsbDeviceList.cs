using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TasollerPlus.Connections.WinUsb
{
    internal static class TasollerPlusWinUsbDeviceList
    {
        private const string ConfigurationManagerLibrary = "CfgMgr32.dll";
        private const string WinUsbServiceName = "WinUSB";
        private const uint FilterService = 0x00000002;
        private const uint FilterPresent = 0x00000100;
        private const uint DoNotGenerateDeviceNode = 0x10000040;
        private const uint DeviceListFlags = FilterService | FilterPresent | DoNotGenerateDeviceNode;
        private const uint EnumerateInterfaceClasses = 0x00000001;
        private const uint PresentDeviceInterfaces = 0x00000000;
        private const uint LocatePresentDeviceNode = 0x00000000;
        private const uint ConfigurationSuccess = 0x00000000;
        private const uint ConfigurationNoSuchDeviceNode = 0x0000000D;
        private const uint ConfigurationBufferTooSmall = 0x0000001A;
        private const uint ConfigurationInvalidData = 0x0000001F;
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

        public static bool IsDeviceAbsent(string instanceId)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || string.IsNullOrEmpty(instanceId))
            {
                return false;
            }

            return LocateDeviceNode(out _, instanceId, LocatePresentDeviceNode) == ConfigurationNoSuchDeviceNode;
        }

        public static IEnumerable<string> ReadPresentInterfacePaths(string instanceId)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || string.IsNullOrEmpty(instanceId))
            {
                yield break;
            }

            for (var classIndex = 0u; ; classIndex++)
            {
                var result = EnumerateDeviceClasses(classIndex, out var interfaceClass, EnumerateInterfaceClasses);
                if (result == ConfigurationInvalidData)
                {
                    continue;
                }

                if (result != ConfigurationSuccess)
                {
                    yield break;
                }

                foreach (var path in ReadPresentInterfacePathsForClass(instanceId, interfaceClass))
                {
                    yield return path;
                }
            }
        }

        private static string[] ReadPresentInterfacePathsForClass(string instanceId, Guid interfaceClass)
        {
            for (var attempt = 0; attempt < MaxEnumerationAttempts; attempt++)
            {
                var sizeResult = GetDeviceInterfaceListSize(out var characterCount, ref interfaceClass, instanceId, PresentDeviceInterfaces);
                if (sizeResult != ConfigurationSuccess)
                {
                    break;
                }

                var buffer = new char[characterCount];
                var listResult = GetDeviceInterfaceList(ref interfaceClass, instanceId, buffer, characterCount, PresentDeviceInterfaces);
                if (listResult == ConfigurationSuccess)
                {
                    return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                }

                if (listResult != ConfigurationBufferTooSmall)
                {
                    break;
                }
            }

            return Array.Empty<string>();
        }

        [DllImport(ConfigurationManagerLibrary, EntryPoint = "CM_Get_Device_ID_List_SizeW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDeviceIdListSize(out uint characterCount, string filter, uint flags);

        [DllImport(ConfigurationManagerLibrary, EntryPoint = "CM_Get_Device_ID_ListW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDeviceIdList(string filter, [Out] char[] buffer, uint characterCount, uint flags);

        [DllImport(ConfigurationManagerLibrary, EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint LocateDeviceNode(out uint deviceNode, string instanceId, uint flags);

        [DllImport(ConfigurationManagerLibrary, EntryPoint = "CM_Enumerate_Classes", ExactSpelling = true)]
        private static extern uint EnumerateDeviceClasses(uint classIndex, out Guid interfaceClass, uint flags);

        [DllImport(ConfigurationManagerLibrary, EntryPoint = "CM_Get_Device_Interface_List_SizeW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDeviceInterfaceListSize(out uint characterCount, ref Guid interfaceClass, string instanceId, uint flags);

        [DllImport(ConfigurationManagerLibrary, EntryPoint = "CM_Get_Device_Interface_ListW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetDeviceInterfaceList(ref Guid interfaceClass, string instanceId, [Out] char[] buffer, uint characterCount, uint flags);
    }
}
