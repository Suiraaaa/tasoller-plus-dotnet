using System;
using System.Linq;

namespace TasollerPlus
{
    internal sealed class TasollerPlusWinUsbTransport : ITasollerPlusTransport
    {
        private const string TasollerPlusUsbDeviceId = @"USB\VID_0E8F&PID_1231";
        private const string SingleInterfaceDevicePrefix = TasollerPlusUsbDeviceId + @"\";
        private const string CompositeInterfaceZeroPrefix = TasollerPlusUsbDeviceId + @"&MI_00\";
        private const int NoMatchingDeviceCount = 0;
        private const int SingleMatchingDeviceCount = 1;

        private readonly Func<string[]?> _readPresentDeviceInstanceIds;

        public TasollerPlusWinUsbTransport(Func<string[]?> readPresentDeviceInstanceIds)
        {
            _readPresentDeviceInstanceIds = readPresentDeviceInstanceIds;
        }

        public TasollerPlusTransportOpenResult Open(out ITasollerPlusConnection? connection)
        {
            connection = null;
            var instanceIds = _readPresentDeviceInstanceIds();
            if (instanceIds is null)
            {
                return TasollerPlusTransportOpenResult.Failed;
            }

            var matchingCount = instanceIds.Count(IsTasollerPlusDeviceInstance);
            return matchingCount switch
            {
                NoMatchingDeviceCount => TasollerPlusTransportOpenResult.DeviceNotFound,
                SingleMatchingDeviceCount => TasollerPlusTransportOpenResult.Failed,
                _ => TasollerPlusTransportOpenResult.MultipleDevicesFound
            };
        }

        private static bool IsTasollerPlusDeviceInstance(string instanceId)
        {
            return instanceId.StartsWith(SingleInterfaceDevicePrefix, StringComparison.OrdinalIgnoreCase) || instanceId.StartsWith(CompositeInterfaceZeroPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
