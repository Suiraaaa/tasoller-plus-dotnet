using System;

namespace TasollerPlus.Connections.WinUsb
{
    internal sealed class TasollerPlusWinUsbTransport : ITasollerPlusTransport
    {
        private const string TasollerPlusUsbDeviceId = @"USB\VID_0E8F&PID_1231";
        private const string SingleInterfaceDevicePrefix = TasollerPlusUsbDeviceId + @"\";
        private const string CompositeInterfaceZeroPrefix = TasollerPlusUsbDeviceId + @"&MI_00\";

        private readonly Func<string[]?> _readPresentDeviceInstanceIds;
        private readonly Func<string, ITasollerPlusConnection?> _openConnection;

        public TasollerPlusWinUsbTransport() : this(TasollerPlusWinUsbDeviceList.ReadPresentDeviceInstanceIds)
        {
        }

        public TasollerPlusWinUsbTransport(Func<string[]?> readPresentDeviceInstanceIds, Func<string, ITasollerPlusConnection?>? openConnection = null)
        {
            _readPresentDeviceInstanceIds = readPresentDeviceInstanceIds;
            _openConnection = openConnection ?? TasollerPlusWinUsbConnection.Open;
        }

        public TasollerPlusDeviceDetectionResult DetectDevice() => FindMatchingDevice().Result;

        public TasollerPlusTransportOpenResult Open()
        {
            var (result, instanceId) = FindMatchingDevice();
            return result switch
            {
                TasollerPlusDeviceDetectionResult.DeviceFound when instanceId != null => TasollerPlusTransportOpenResult.FromConnection(_openConnection(instanceId)),
                TasollerPlusDeviceDetectionResult.DeviceNotFound => TasollerPlusTransportOpenResult.DeviceNotFound,
                TasollerPlusDeviceDetectionResult.MultipleDevicesFound => TasollerPlusTransportOpenResult.MultipleDevicesFound,
                _ => TasollerPlusTransportOpenResult.Failed
            };
        }

        private (TasollerPlusDeviceDetectionResult Result, string? InstanceId) FindMatchingDevice()
        {
            var instanceIds = _readPresentDeviceInstanceIds();
            if (instanceIds is null)
            {
                return (TasollerPlusDeviceDetectionResult.DetectionFailed, null);
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
                    return (TasollerPlusDeviceDetectionResult.MultipleDevicesFound, null);
                }

                matchingInstanceId = instanceId;
            }

            return matchingInstanceId is null
                ? (TasollerPlusDeviceDetectionResult.DeviceNotFound, null)
                : (TasollerPlusDeviceDetectionResult.DeviceFound, matchingInstanceId);
        }

        private static bool IsTasollerPlusDeviceInstance(string instanceId)
        {
            return instanceId.StartsWith(SingleInterfaceDevicePrefix, StringComparison.OrdinalIgnoreCase) || instanceId.StartsWith(CompositeInterfaceZeroPrefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
