namespace TasollerPlus
{
    /// <summary>The outcome of checking for Tasoller Plus devices in WinUSB mode.</summary>
    public enum TasollerPlusDeviceDetectionResult
    {
        /// <summary>The device list could not be read.</summary>
        DetectionFailed,

        /// <summary>No matching devices were found.</summary>
        DeviceNotFound,

        /// <summary>Exactly one matching device was found.</summary>
        DeviceFound,

        /// <summary>At least two matching devices were found.</summary>
        MultipleDevicesFound
    }
}
