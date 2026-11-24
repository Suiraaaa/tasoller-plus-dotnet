namespace TasollerPlus
{
    /// <summary>現在の通信状態、または通信が終了した理由を表します。</summary>
    public enum TasollerPlusConnectionStatus
    {
        /// <summary>通信を開始していないか、利用側の操作で通信を終了した状態です。</summary>
        Disconnected,

        /// <summary>対応機器と通信中です。</summary>
        Connected,

        /// <summary>機器の取り外しを検知して通信を終了しました。</summary>
        DeviceRemoved,

        /// <summary>入力の受信に失敗して通信を終了しました。</summary>
        InputReceiveError,

        /// <summary>LEDデータの送信に失敗して通信を終了しました。</summary>
        LedSendError
    }
}
