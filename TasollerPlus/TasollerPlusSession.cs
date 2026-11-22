using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus
{
    internal sealed class TasollerPlusSession : IDisposable
    {
        private const int LedReportSignalCapacity = 1;

        private readonly CancellationTokenSource _connectionCancellation;
        private readonly Task _connectionTask;
        private readonly object _stateLock;
        private readonly SemaphoreSlim _pendingLedReportSignal;
        private readonly int _inputBufferCapacity;
        private readonly Func<long> _getTimestamp;
        private readonly Queue<TasollerPlusInputFrame> _pendingInputs;
        private byte[]? _pendingLedReport;
        private TasollerPlusInputFrame? _latestInput;
        private long _droppedInputCount;
        private long _lastSequenceNumber;
        private TasollerPlusConnectionStatus _status;

        public TasollerPlusSession(ITasollerPlusConnection connection, int inputBufferCapacity, Func<long> getTimestamp, long lastSequenceNumber)
        {
            _inputBufferCapacity = inputBufferCapacity;
            _getTimestamp = getTimestamp;
            _lastSequenceNumber = lastSequenceNumber;

            _connectionCancellation = new CancellationTokenSource();
            _stateLock = new object();
            _pendingLedReportSignal = new SemaphoreSlim(initialCount: 0, maxCount: LedReportSignalCapacity);
            _pendingInputs = new Queue<TasollerPlusInputFrame>(_inputBufferCapacity);
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
                frame = _latestInput.GetValueOrDefault();
                return _latestInput.HasValue;
            }
        }

        public TasollerPlusInputReadResult ReadPendingInputs(TasollerPlusInputFrame[] destination)
        {
            lock (_stateLock)
            {
                var count = Math.Min(destination.Length, _pendingInputs.Count);
                for (var index = 0; index < count; index++)
                {
                    destination[index] = _pendingInputs.Dequeue();
                }

                var result = new TasollerPlusInputReadResult(count, _droppedInputCount);
                _droppedInputCount = default;
                return result;
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
                catch
                {
                    EndConnection(errorStatus);
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
            while (!cancellationToken.IsCancellationRequested)
            {
                await _pendingLedReportSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                var report = TakePendingLedReport();
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
            }
        }

        private byte[]? TakePendingLedReport()
        {
            lock (_stateLock)
            {
                var report = _pendingLedReport;
                _pendingLedReport = null;
                return report;
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

                _latestInput = null;
                _pendingInputs.Clear();
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

                if (_pendingInputs.Count == _inputBufferCapacity)
                {
                    _pendingInputs.Dequeue();
                    _droppedInputCount++;
                }

                _pendingInputs.Enqueue(frame);
                _latestInput = frame;
                _lastSequenceNumber = frame.SequenceNumber;
            }
        }
    }
}
