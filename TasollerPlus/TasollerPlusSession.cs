using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus
{
    internal sealed class TasollerPlusSession : IDisposable
    {
        private readonly ITasollerPlusConnection _connection;
        private readonly CancellationTokenSource _receiveCancellation;
        private readonly Task _receiveTask;
        private readonly object _inputLock;
        private readonly int _inputBufferCapacity;
        private readonly Func<long> _getTimestamp;
        private readonly Queue<TasollerPlusInputFrame> _pendingInputs;
        private TasollerPlusInputFrame? _latestInput;
        private long _droppedInputCount;
        private long _lastSequenceNumber;

        public TasollerPlusSession(ITasollerPlusConnection connection, int inputBufferCapacity, Func<long> getTimestamp)
        {
            _connection = connection;
            _inputBufferCapacity = inputBufferCapacity;
            _getTimestamp = getTimestamp;

            _receiveCancellation = new CancellationTokenSource();
            _inputLock = new object();
            _pendingInputs = new Queue<TasollerPlusInputFrame>(_inputBufferCapacity);

            var cancellationToken = _receiveCancellation.Token;
            _receiveTask = Task.Run(() => ReceiveInputsAsync(cancellationToken), cancellationToken);
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
                _connection.Dispose();
            }
        }

        private async Task ReceiveInputsAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[TasollerPlusInputReport.Length];

            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await _connection.ReadInputAsync(buffer, cancellationToken).ConfigureAwait(false);
                var receivedTimestamp = _getTimestamp();
                var sequenceNumber = _lastSequenceNumber + 1;
                var decodedInput = TasollerPlusInputReport.Decode(buffer.AsSpan()[..bytesRead], receivedTimestamp, sequenceNumber);
                if (!decodedInput.HasValue)
                {
                    return;
                }

                StoreInput(decodedInput.Value);
            }
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
