using System;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus
{
    internal sealed class TasollerPlusSession : IDisposable
    {
        private readonly ITasollerPlusConnection _connection;
        private readonly CancellationTokenSource _receiveCancellation = new CancellationTokenSource();
        private readonly Task _receiveTask;
        private readonly object _latestInputLock = new object();
        private TasollerPlusInputFrame? _latestInput;

        public TasollerPlusSession(ITasollerPlusConnection connection)
        {
            _connection = connection;
            var cancellationToken = _receiveCancellation.Token;
            _receiveTask = Task.Run(() => ReceiveInputsAsync(cancellationToken), cancellationToken);
        }

        public bool TryGetLatestInput(out TasollerPlusInputFrame frame)
        {
            lock (_latestInputLock)
            {
                frame = _latestInput.GetValueOrDefault();
                return _latestInput.HasValue;
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
                var input = TasollerPlusInputReport.Decode(buffer.AsSpan()[..bytesRead]);
                if (!input.HasValue)
                {
                    return;
                }

                lock (_latestInputLock)
                {
                    _latestInput = input;
                }
            }
        }
    }
}
