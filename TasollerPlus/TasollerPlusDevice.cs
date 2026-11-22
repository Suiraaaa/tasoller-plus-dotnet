using System;

namespace TasollerPlus
{
    public sealed class TasollerPlusDevice : IDisposable
    {
        private readonly ITasollerPlusTransport _transport;
        private TasollerPlusSession? _session;

        internal TasollerPlusDevice(ITasollerPlusTransport transport)
        {
            _transport = transport;
        }

        public TasollerPlusConnectResult Connect()
        {
            if (_session != null)
            {
                return TasollerPlusConnectResult.Success;
            }

            var result = _transport.Open(out var connection);
            if (result == TasollerPlusConnectResult.Success && connection != null)
            {
                _session = new TasollerPlusSession(connection);
            }

            return result;
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

        public void Dispose()
        {
            var session = _session;
            _session = null;
            session?.Dispose();
        }
    }
}
