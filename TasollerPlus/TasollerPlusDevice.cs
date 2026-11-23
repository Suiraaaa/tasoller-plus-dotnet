using System;
using System.Diagnostics;

using TasollerPlus.Connections;
using TasollerPlus.Connections.WinUsb;
using TasollerPlus.Protocol;

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
        private bool _isDisposed;

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

        public int InputBufferCapacity => _inputBufferCapacity;

        public TasollerPlusConnectionStatus Status => _session?.Status ?? _disconnectedStatus;

        /// <summary>Checks for Tasoller Plus devices in WinUSB mode without opening a connection.</summary>
        /// <remarks>The result reflects the device list at the time of the call. <see cref="Connect"/> checks the list again when opening a connection.</remarks>
        /// <exception cref="ObjectDisposedException">This instance has been disposed.</exception>
        public TasollerPlusDeviceDetectionResult DetectDevice()
        {
            ThrowIfDisposed();
            return _transport.DetectDevice();
        }

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
