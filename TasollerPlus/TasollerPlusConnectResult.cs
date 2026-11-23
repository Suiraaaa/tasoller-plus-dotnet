namespace TasollerPlus
{
    /// <summary><see cref="TasollerPlusDevice.Connect"/> の結果を表します。</summary>
    public enum TasollerPlusConnectResult
    {
        /// <summary>対応機器との通信を開始しました。</summary>
        Success,

        /// <summary>このインスタンスはすでに通信中です。</summary>
        AlreadyConnected,

        /// <summary>WinUSBモードの対応機器が見つかりませんでした。</summary>
        DeviceNotFound,

        /// <summary>WinUSBモードの対応機器が2台以上見つかりました。</summary>
        MultipleDevicesFound,

        /// <summary>機器一覧の取得、または対応機器との通信の開始に失敗しました。</summary>
        ConnectionFailed
    }
}
