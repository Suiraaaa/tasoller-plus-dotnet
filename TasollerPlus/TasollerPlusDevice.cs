using System;
using System.Diagnostics;

using TasollerPlus.Connections;
using TasollerPlus.Connections.WinUsb;
using TasollerPlus.Protocol;

namespace TasollerPlus
{
    /// <summary>WinUSBモードの Tasoller Plus を検出し、接続、入力取得、LED制御を行います。</summary>
    /// <remarks>1インスタンスで1台の接続を扱います。同じインスタンスの公開メンバーは、利用側で呼び出しを直列化してください。</remarks>
    public sealed class TasollerPlusDevice : IDisposable
    {
        private const int DefaultInputBufferCapacity = 1024;

        private readonly ITasollerPlusTransport _transport;
        private readonly int _inputBufferCapacity;
        private readonly Func<long> _getTimestamp;
        private TasollerPlusSession? _session;
        private long _lastSequenceNumber;
        private TasollerPlusConnectionStatus _disconnectedStatus;
        private bool _isDisposed;

        /// <summary>未接続のデバイスインスタンスを作成します。</summary>
        public TasollerPlusDevice() : this(new TasollerPlusWinUsbTransport())
        {
        }

        internal TasollerPlusDevice(ITasollerPlusTransport transport, int inputBufferCapacity = DefaultInputBufferCapacity, Func<long>? getTimestamp = null)
        {
            _transport = transport;
            _inputBufferCapacity = inputBufferCapacity;

            _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;

            if (inputBufferCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(inputBufferCapacity));
            }
        }

        /// <summary>未読入力を保持できる最大件数を取得します。</summary>
        /// <value>未読入力キューの最大件数。現在は1,024件です。</value>
        /// <remarks>上限を超えると、最も古い未読入力から破棄されます。</remarks>
        public int InputBufferCapacity => _inputBufferCapacity;

        /// <summary>現在の通信状態を取得します。</summary>
        /// <value>接続中、未接続、または機器の取り外しや送受信エラーによる終了理由を示す値です。</value>
        public TasollerPlusConnectionStatus Status => _session?.Status ?? _disconnectedStatus;

        /// <summary>通信を開かずに、WinUSBモードの Tasoller Plus を検索します。</summary>
        /// <returns>対応機器が0台、1台、複数台、または機器一覧の取得に失敗したことを示す値です。</returns>
        /// <remarks>結果は呼び出し時点の機器一覧に基づきます。<see cref="Connect"/> は通信を開く際に再検索します。</remarks>
        /// <exception cref="ObjectDisposedException">インスタンスが破棄済みです。</exception>
        public TasollerPlusDeviceDetectionResult DetectDevice()
        {
            ThrowIfDisposed();
            return _transport.DetectDevice();
        }

        /// <summary>WinUSBモードの対応機器が1台見つかった場合に通信を開始します。</summary>
        /// <returns>接続成功、接続済み、機器なし、複数台検出、または接続失敗を示す値です。</returns>
        /// <remarks>同じインスタンスを使って再接続できます。</remarks>
        /// <exception cref="ObjectDisposedException">インスタンスが破棄済みです。</exception>
        public TasollerPlusConnectResult Connect()
        {
            ThrowIfDisposed();

            if (Status == TasollerPlusConnectionStatus.Connected)
            {
                return TasollerPlusConnectResult.AlreadyConnected;
            }

            Disconnect();

            var openResult = _transport.Open();
            var connection = openResult.Connection;
            if (connection != null)
            {
                _session = new TasollerPlusSession(connection, InputBufferCapacity, _getTimestamp, _lastSequenceNumber);
            }

            return openResult.ConnectResult;
        }

        /// <summary>現在の通信を終了します。</summary>
        /// <remarks>通信の終了時に、最新入力、未読入力、LEDの送信と再送に使う色指定が破棄されます。</remarks>
        public void Disconnect()
        {
            var session = _session;
            _session = null;
            if (session is null)
            {
                return;
            }

            try
            {
                session.Dispose();
            }
            finally
            {
                _lastSequenceNumber = session.LastSequenceNumber;
                _disconnectedStatus = session.Status;
            }
        }

        /// <summary>最後に受信した有効な入力を取得します。未読入力キューの内容は消費しません。</summary>
        /// <param name="frame">最新の入力フレーム。取得できない場合は既定値です。</param>
        /// <returns>取得できた場合は <see langword="true"/>、取得できない場合は <see langword="false"/> です。</returns>
        public bool TryGetLatestInput(out TasollerPlusInputFrame frame)
        {
            if (_session is { } session)
            {
                return session.TryGetLatestInput(out frame);
            }

            frame = default;
            return false;
        }

        /// <summary>未読入力を古い順に配列へコピーし、コピーした分を未読入力キューから取り除きます。</summary>
        /// <param name="destination">入力フレームのコピー先。配列に入らなかった未読入力は次回の取得に持ち越します。</param>
        /// <returns>コピーした件数と、前回の取得以降にキューからあふれて破棄された件数です。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> が <see langword="null"/> です。</exception>
        public TasollerPlusInputReadResult ReadPendingInputs(TasollerPlusInputFrame[] destination)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            return _session?.ReadPendingInputs(destination) ?? default;
        }

        /// <summary>スライダーと左右のAIRタワーのLED色を一括指定します。</summary>
        /// <param name="slider">スライダーの31枠に指定するRGB色です。</param>
        /// <param name="leftAir">左AIRタワーの3枠に指定するRGB色です。</param>
        /// <param name="rightAir">右AIRタワーの3枠に指定するRGB色です。</param>
        /// <returns>色指定を受け付けた場合は <see langword="true"/>、通信中でない場合は <see langword="false"/> です。</returns>
        /// <remarks>送信待ちの指定は最新の指定に置き換わります。最後に送信できた色は通信中に約1秒間隔で再送します。</remarks>
        /// <exception cref="ArgumentException">いずれかの色配列が規定の要素数を満たしていません。</exception>
        public bool TrySetLedColors(RgbColor[] slider, RgbColor[] leftAir, RgbColor[] rightAir)
        {
            var report = TasollerPlusLedReport.Encode(slider, leftAir, rightAir);
            return _session?.TrySetPendingLedReport(report) ?? false;
        }

        /// <summary>現在の通信を終了し、このインスタンスを破棄します。</summary>
        /// <remarks>破棄後の <see cref="DetectDevice"/> と <see cref="Connect"/> は <see cref="ObjectDisposedException"/> を送出します。</remarks>
        public void Dispose()
        {
            _isDisposed = true;
            Disconnect();
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(TasollerPlusDevice));
            }
        }
    }
}
