namespace TasollerPlus
{
    /// <summary><see cref="TasollerPlusDevice.DetectDevice"/> によるWinUSBモードの対応機器の検索結果を表します。</summary>
    public enum TasollerPlusDeviceDetectionResult
    {
        /// <summary>機器一覧を取得できませんでした。</summary>
        DetectionFailed,

        /// <summary>対応機器が見つかりませんでした。</summary>
        DeviceNotFound,

        /// <summary>対応機器が1台見つかりました。</summary>
        DeviceFound,

        /// <summary>対応機器が2台以上見つかりました。</summary>
        MultipleDevicesFound
    }
}
