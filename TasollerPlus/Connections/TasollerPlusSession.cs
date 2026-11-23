using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using TasollerPlus.Protocol;

namespace TasollerPlus.Connections
{
    internal sealed class TasollerPlusSession : IDisposable
    {
        private const int LedReportSignalCapacity = 1;
        private const int DefaultLedResendIntervalSeconds = 1;
        private static readonly TimeSpan DefaultLedResendInterval = TimeSpan.FromSeconds(DefaultLedResendIntervalSeconds);

        private readonly CancellationTokenSource _connectionCancellation;
        private readonly Task _connectionTask;
        private readonly object _stateLock;
        private readonly SemaphoreSlim _pendingLedReportSignal;
        private readonly Func<long> _getTimestamp;
        private readonly TasollerPlusInputBuffer _inputBuffer;
        private byte[]? _pendingLedReport;
        private long _lastSequenceNumber;
        private TasollerPlusConnectionStatus _status;

        public TasollerPlusSession(ITasollerPlusConnection connection, int inputBufferCapacity, Func<long> getTimestamp, long lastSequenceNumber)
        {
            _getTimestamp = getTimestamp;
            _lastSequenceNumber = lastSequenceNumber;

            _connectionCancellation = new CancellationTokenSource();
            _stateLock = new object();
            _pendingLedReportSignal = new SemaphoreSlim(initialCount: 0, maxCount: LedReportSignalCapacity);
            _inputBuffer = new TasollerPlusInputBuffer(inputBufferCapacity);
            _status = TasollerPlusConnectionStatus.Connected;

            _connectionTask = RunConnectionAsync(connection, _connectionCancellation.Token);
        }

        public long LastSequenceNumber
        {
            get
            {
                lock (_stateLock)
                {
                    return _lastSequenceNumber;
                }
            }
        }

        public TasollerPlusConnectionStatus Status
        {
            get
            {
                lock (_stateLock)
                {
                    return _status;
                }
            }
        }

        public bool TryGetLatestInput(out TasollerPlusInputFrame frame)
        {
            lock (_stateLock)
            {
                return _inputBuffer.TryGetLatest(out frame);
            }
        }

        public TasollerPlusInputReadResult ReadPendingInputs(TasollerPlusInputFrame[] destination)
        {
            lock (_stateLock)
            {
                return _inputBuffer.ReadPending(destination);
            }
        }

        public bool TrySetPendingLedReport(byte[] report)
        {
            lock (_stateLock)
            {
                if (_status != TasollerPlusConnectionStatus.Connected)
                {
                    return false;
                }

                var shouldSignalSender = _pendingLedReport is null;
                _pendingLedReport = report;
                if (shouldSignalSender)
                {
                    _pendingLedReportSignal.Release();
                }

                return true;
            }
        }

        public void Dispose()
        {
            try
            {
                EndConnection(TasollerPlusConnectionStatus.Disconnected);
                _connectionTask.GetAwaiter().GetResult();
            }
            finally
            {
                _connectionCancellation.Dispose();
                _pendingLedReportSignal.Dispose();
            }
        }

        private async Task RunConnectionAsync(ITasollerPlusConnection connection, CancellationToken cancellationToken)
        {
            using (connection)
            {
                var receiveTask = RunTransferAsync(() => ReceiveInputsAsync(connection, cancellationToken), TasollerPlusConnectionStatus.InputReceiveError);
                var sendTask = RunTransferAsync(() => SendLedReportsAsync(connection, cancellationToken), TasollerPlusConnectionStatus.LedSendError);
                await Task.WhenAll(receiveTask, sendTask).ConfigureAwait(false);
            }
        }

        private Task RunTransferAsync(Func<Task> transfer, TasollerPlusConnectionStatus errorStatus)
        {
            return Task.Run(async () =>
            {
                try
                {
                    await transfer().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_connectionCancellation.IsCancellationRequested)
                {
                    // Closing the connection cancels pending I/O.
                }
                catch (Exception exception)
                {
                    EndConnection(errorStatus);

                    if (exception is IOException)
                    {
                        return;
                    }

                    throw;
                }
            });
        }

        private async Task ReceiveInputsAsync(ITasollerPlusConnection connection, CancellationToken cancellationToken)
        {
            var buffer = new byte[TasollerPlusInputReport.Length];

            while (!cancellationToken.IsCancellationRequested)
            {
                var transfer = await connection.ReadInputAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (transfer.Status != TasollerPlusTransferStatus.Succeeded)
                {
                    EndConnectionAfterTransferFailure(transfer.Status, TasollerPlusConnectionStatus.InputReceiveError);
                    return;
                }

                var receivedTimestamp = _getTimestamp();
                var sequenceNumber = _lastSequenceNumber + 1;
                var decodedInput = TasollerPlusInputReport.Decode(buffer.AsSpan()[..transfer.BytesRead], receivedTimestamp, sequenceNumber);
                if (decodedInput.HasValue)
                {
                    StoreInput(decodedInput.Value);
                }
            }
        }

        private async Task SendLedReportsAsync(ITasollerPlusConnection connection, CancellationToken cancellationToken)
        {
            var lastSentReport = default(byte[]?);
            while (!cancellationToken.IsCancellationRequested)
            {
                var timeout = lastSentReport is null ? Timeout.InfiniteTimeSpan : DefaultLedResendInterval;
                var pendingReportSignaled = await _pendingLedReportSignal.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

                byte[]? report;
                lock (_stateLock)
                {
                    if (_status != TasollerPlusConnectionStatus.Connected)
                    {
                        return;
                    }

                    // A report queued as the wait timed out still has a signal to consume.
                    if (!pendingReportSignaled && _pendingLedReport != null)
                    {
                        continue;
                    }

                    report = _pendingLedReport ?? lastSentReport;
                    _pendingLedReport = null;
                }

                if (report is null)
                {
                    return;
                }

                var transferStatus = await connection.WriteOutputAsync(report, cancellationToken).ConfigureAwait(false);
                if (transferStatus != TasollerPlusTransferStatus.Succeeded)
                {
                    EndConnectionAfterTransferFailure(transferStatus, TasollerPlusConnectionStatus.LedSendError);
                    return;
                }

                lastSentReport = report;
            }
        }

        private void EndConnectionAfterTransferFailure(TasollerPlusTransferStatus transferStatus, TasollerPlusConnectionStatus failureStatus)
        {
            var status = transferStatus == TasollerPlusTransferStatus.DeviceRemoved ? TasollerPlusConnectionStatus.DeviceRemoved : failureStatus;
            EndConnection(status);
        }

        private void EndConnection(TasollerPlusConnectionStatus status)
        {
            lock (_stateLock)
            {
                if (_status != TasollerPlusConnectionStatus.Connected)
                {
                    return;
                }

                _inputBuffer.Clear();
                _pendingLedReport = null;
                _status = status;
            }

            _connectionCancellation.Cancel();
        }

        private void StoreInput(TasollerPlusInputFrame frame)
        {
            lock (_stateLock)
            {
                if (_status != TasollerPlusConnectionStatus.Connected)
                {
                    return;
                }

                _inputBuffer.Add(frame);
                _lastSequenceNumber = frame.SequenceNumber;
            }
        }
    }
}
