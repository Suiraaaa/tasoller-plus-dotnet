using System;

namespace TasollerPlus
{
    /// <summary>スライダー、AIRセンサー、FNボタンの状態を保持する、読み取り専用の入力フレームです。</summary>
    /// <remarks><see cref="TasollerPlusDevice.TryGetLatestInput"/> または <see cref="TasollerPlusDevice.ReadPendingInputs"/> で取得します。</remarks>
    public readonly struct TasollerPlusInputFrame
    {
        private const int BottomAirBeamMask = 0b0000_0001;
        internal const int AirBeamCount = 6;

        private readonly ReadOnlyMemory<byte> _sliderValues;
        private readonly byte _blockedAirBeamMask;
        private readonly bool _isFn1Pressed;
        private readonly bool _isFn2Pressed;
        private readonly long _receivedTimestamp;
        private readonly long _sequenceNumber;

        internal TasollerPlusInputFrame(ReadOnlySpan<byte> sliderValues, bool isFn1Pressed, bool isFn2Pressed, byte blockedAirBeamMask, long receivedTimestamp, long sequenceNumber)
        {
            _isFn1Pressed = isFn1Pressed;
            _isFn2Pressed = isFn2Pressed;
            _blockedAirBeamMask = blockedAirBeamMask;
            _receivedTimestamp = receivedTimestamp;
            _sequenceNumber = sequenceNumber;

            _sliderValues = sliderValues.ToArray();
        }

        /// <summary>この入力をPCで受信した時点のタイムスタンプを取得します。</summary>
        /// <value><see cref="System.Diagnostics.Stopwatch.GetTimestamp()"/> で取得したティック値です。</value>
        public long ReceivedTimestamp => _receivedTimestamp;

        /// <summary>有効な入力の受信順を表す連番を取得します。</summary>
        /// <remarks>最初の有効入力を1として、同じデバイスインスタンスでは再接続をまたいで1ずつ増加します。</remarks>
        public long SequenceNumber => _sequenceNumber;

        /// <summary>FN1ボタンが押されているかどうかを取得します。</summary>
        public bool IsFn1Pressed => _isFn1Pressed;

        /// <summary>FN2ボタンが押されているかどうかを取得します。</summary>
        public bool IsFn2Pressed => _isFn2Pressed;

        /// <summary>指定したスライダー領域のアナログ入力値を取得します。</summary>
        /// <param name="index">32領域の添字。範囲は0〜31です。</param>
        /// <returns>指定した領域のアナログ入力値。範囲は0〜255です。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> がフレームのスライダー領域の範囲外です。</exception>
        public byte GetSliderValue(int index)
        {
            if ((uint)index >= (uint)_sliderValues.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _sliderValues.Span[index];
        }

        /// <summary>指定したAIRセンサーのビームが遮断されているかどうかを取得します。</summary>
        /// <param name="index">センサーの添字。最下段を0、最上段を5とします。</param>
        /// <returns>遮断されている場合は <see langword="true"/>、遮断されていない場合は <see langword="false"/> です。</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> が0〜5の範囲外です。</exception>
        public bool IsAirBlocked(int index)
        {
            if ((uint)index >= AirBeamCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var beamMask = BottomAirBeamMask << index;
            return (_blockedAirBeamMask & beamMask) != 0;
        }
    }
}
