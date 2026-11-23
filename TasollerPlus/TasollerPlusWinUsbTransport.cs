using System;

namespace TasollerPlus
{
    internal sealed class TasollerPlusWinUsbTransport : ITasollerPlusTransport
    {
        private const string TasollerPlusUsbDeviceId = @"USB\VID_0E8F&PID_1231";
        private const string SingleInterfaceDevicePrefix = TasollerPlusUsbDeviceId + @"\";
        private const string CompositeInterfaceZeroPrefix = TasollerPlusUsbDeviceId + @"&MI_00\";

        private readonly Func<string[]?> _readPresentDeviceInstanceIds;
        private readonly Func<string, ITasollerPlusConnection?> _openConnection;

        public TasollerPlusWinUsbTransport(Func<string[]?> readPresentDeviceInstanceIds, Func<string, ITasollerPlusConnection?>? openConnection = null)
        {
            _readPresentDeviceInstanceIds = readPresentDeviceInstanceIds;
            _openConnection = openConnection ?? TasollerPlusWinUsbConnection.Open;
        }

        public TasollerPlusTransportOpenResult Open(out ITasollerPlusConnection? connection)
        {
            connection = null;
            var instanceIds = _readPresentDeviceInstanceIds();
            if (instanceIds is null)
            {
                return TasollerPlusTransportOpenResult.Failed;
            }

            var matchingInstanceId = default(string);
            foreach (var instanceId in instanceIds)
            {
                if (!IsTasollerPlusDeviceInstance(instanceId))
                {
                    continue;
                }

                if (matchingInstanceId != null)
                {
                    return TasollerPlusTransportOpenResult.MultipleDevicesFound;
                }

                matchingInstanceId = instanceId;
            }

            if (matchingInstanceId is null)
            {
                return TasollerPlusTransportOpenResult.DeviceNotFound;
            }

            connection = _openConnection(matchingInstanceId);
            return connection is null ? TasollerPlusTransportOpenResult.Failed : TasollerPlusTransportOpenResult.Opened;
        }

        private static bool IsTasollerPlusDeviceInstance(string instanceId)
        {
            return instanceId.StartsWith(SingleInterfaceDevicePrefix, StringComparison.OrdinalIgnoreCase) || instanceId.StartsWith(CompositeInterfaceZeroPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
