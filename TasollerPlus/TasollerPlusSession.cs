using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus
{
    internal sealed class TasollerPlusSession : IDisposable
    {
        private readonly CancellationTokenSource _receiveCancellation;
        private readonly Task _receiveTask;
        private readonly object _inputLock;
        private readonly int _inputBufferCapacity;
        private readonly Func<long> _getTimestamp;
        private readonly Queue<TasollerPlusInputFrame> _pendingInputs;
        private ITasollerPlusConnection? _connection;
        private TasollerPlusInputFrame? _latestInput;
        private long _droppedInputCount;
        private long _lastSequenceNumber;
        private TasollerPlusConnectionStatus _status;

        public TasollerPlusSession(ITasollerPlusConnection connection, int inputBufferCapacity, Func<long> getTimestamp, long lastSequenceNumber)
        {
            _connection = connection;
            _inputBufferCapacity = inputBufferCapacity;
            _getTimestamp = getTimestamp;
            _lastSequenceNumber = lastSequenceNumber;

            _receiveCancellation = new CancellationTokenSource();
            _inputLock = new object();
            _pendingInputs = new Queue<TasollerPlusInputFrame>(_inputBufferCapacity);
            _status = TasollerPlusConnectionStatus.Connected;

            var cancellationToken = _receiveCancellation.Token;
            _receiveTask = Task.Run(() => ReceiveInputsAsync(connection, cancellationToken), cancellationToken);
        }

        public long LastSequenceNumber
        {
            get
            {
                lock (_inputLock)
                {
                    return _lastSequenceNumber;
                }
            }
        }

        public TasollerPlusConnectionStatus Status
        {
            get
            {
                lock (_inputLock)
                {
                    return _status;
                }
            }
        }

        public bool TryGetLatestInput(out TasollerPlusInputFrame frame)
        {
            lock (_inputLock)
            {
                frame = _latestInput.GetValueOrDefault();
                return _latestInput.HasValue;
            }
        }

        public TasollerPlusInputReadResult ReadPendingInputs(TasollerPlusInputFrame[] destination)
        {
            lock (_inputLock)
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

        public void Dispose()
        {
            try
            {
                _receiveCancellation.Cancel();
                _receiveTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (_receiveCancellation.IsCancellationRequested)
            {
                // A pending read is canceled when the session closes.
            }
            finally
            {
                _receiveCancellation.Dispose();
                CloseConnection();
            }
        }

        private async Task ReceiveInputsAsync(ITasollerPlusConnection connection, CancellationToken cancellationToken)
        {
            var buffer = new byte[TasollerPlusInputReport.Length];

            while (!cancellationToken.IsCancellationRequested)
            {
                var transfer = await connection.ReadInputAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (transfer.Status != TasollerPlusReadTransferStatus.Succeeded)
                {
                    var status = transfer.Status == TasollerPlusReadTransferStatus.DeviceRemoved ? TasollerPlusConnectionStatus.DeviceRemoved : TasollerPlusConnectionStatus.InputReceiveError;
                    EndConnection(status);
                    return;
                }

                var receivedTimestamp = _getTimestamp();
                var sequenceNumber = _lastSequenceNumber + 1;
                var decodedInput = TasollerPlusInputReport.Decode(buffer.AsSpan()[..transfer.BytesRead], receivedTimestamp, sequenceNumber);
                if (!decodedInput.HasValue)
                {
                    return;
                }

                StoreInput(decodedInput.Value);
            }
        }

        private void EndConnection(TasollerPlusConnectionStatus status)
        {
            lock (_inputLock)
            {
                _latestInput = null;
                _pendingInputs.Clear();
                _status = status;
            }

            CloseConnection();
        }

        private void CloseConnection()
        {
            Interlocked.Exchange(ref _connection, null)?.Dispose();
        }

        private void StoreInput(TasollerPlusInputFrame frame)
        {
            lock (_inputLock)
            {
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
