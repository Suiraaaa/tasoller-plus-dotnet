using System;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus
{
    internal sealed class TasollerPlusSession : IDisposable
    {
        private readonly ITasollerPlusConnection _connection;
        private readonly CancellationTokenSource _readCancellation = new CancellationTokenSource();
        private readonly Task<TasollerPlusInputFrame?> _firstInput;

        public TasollerPlusSession(ITasollerPlusConnection connection)
        {
            _connection = connection;
            var cancellationToken = _readCancellation.Token;
            _firstInput = Task.Run(() => ReadFirstInputAsync(connection, cancellationToken), cancellationToken);
        }

        public bool TryGetLatestInput(out TasollerPlusInputFrame frame)
        {
            if (_firstInput.IsCompletedSuccessfully && _firstInput.Result is TasollerPlusInputFrame received)
            {
                frame = received;
                return true;
            }

            frame = default;
            return false;
        }

        public void Dispose()
        {
            try
            {
                _readCancellation.Cancel();
                _firstInput.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (_readCancellation.IsCancellationRequested)
            {
                // A pending read is canceled when the session closes.
            }
            finally
            {
                _readCancellation.Dispose();
                _connection.Dispose();
            }
        }

        private static async Task<TasollerPlusInputFrame?> ReadFirstInputAsync(ITasollerPlusConnection connection, CancellationToken cancellationToken)
        {
            var buffer = new byte[TasollerPlusInputReport.Length];
            var bytesRead = await connection.ReadInputAsync(buffer, cancellationToken).ConfigureAwait(false);
            return TasollerPlusInputReport.Decode(buffer.AsSpan()[..bytesRead]);
        }
    }
}
