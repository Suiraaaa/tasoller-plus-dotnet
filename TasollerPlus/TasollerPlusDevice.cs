using System;
using System.Diagnostics;

namespace TasollerPlus
{
    public sealed class TasollerPlusDevice : IDisposable
    {
        private const int DefaultInputBufferCapacity = 1024;

        private readonly ITasollerPlusTransport _transport;
        private readonly int _inputBufferCapacity;
        private readonly Func<long> _getTimestamp;
        private TasollerPlusSession? _session;
        private long _lastSequenceNumber;
        private TasollerPlusConnectionStatus _disconnectedStatus;

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

        public int InputBufferCapacity => _inputBufferCapacity;

        public TasollerPlusConnectionStatus Status => _session?.Status ?? _disconnectedStatus;

        public TasollerPlusConnectResult Connect()
        {
            if (Status == TasollerPlusConnectionStatus.Connected)
            {
                return TasollerPlusConnectResult.AlreadyConnected;
            }

            Disconnect();

            var openResult = _transport.Open(out var connection);
            switch (openResult)
            {
                case TasollerPlusTransportOpenResult.DeviceNotFound:
                    return TasollerPlusConnectResult.DeviceNotFound;
                case TasollerPlusTransportOpenResult.MultipleDevicesFound:
                    return TasollerPlusConnectResult.MultipleDevicesFound;
                case TasollerPlusTransportOpenResult.Opened when connection != null:
                    _session = new TasollerPlusSession(connection, InputBufferCapacity, _getTimestamp, _lastSequenceNumber);
                    return TasollerPlusConnectResult.Success;
                default:
                    return TasollerPlusConnectResult.ConnectionFailed;
            }
        }

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

        public bool TryGetLatestInput(out TasollerPlusInputFrame frame)
        {
            if (_session is { } session)
            {
                return session.TryGetLatestInput(out frame);
            }

            frame = default;
            return false;
        }

        public TasollerPlusInputReadResult ReadPendingInputs(TasollerPlusInputFrame[] destination)
        {
            if (destination is null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            return _session?.ReadPendingInputs(destination) ?? default;
        }

        public bool TrySetLedColors(RgbColor[] slider, RgbColor[] leftAir, RgbColor[] rightAir)
        {
            var report = TasollerPlusLedReport.Encode(slider, leftAir, rightAir);
            return _session?.TrySetPendingLedReport(report) ?? false;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
