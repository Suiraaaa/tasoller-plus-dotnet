namespace TasollerPlus.Connections
{
    internal readonly struct TasollerPlusTransportOpenResult
    {
        private readonly TasollerPlusConnectResult? _connectResult;
        private readonly ITasollerPlusConnection? _connection;

        private TasollerPlusTransportOpenResult(TasollerPlusConnectResult connectResult, ITasollerPlusConnection? connection)
        {
            _connectResult = connectResult;
            _connection = connection;
        }

        public TasollerPlusConnectResult ConnectResult => _connectResult ?? TasollerPlusConnectResult.ConnectionFailed;

        public ITasollerPlusConnection? Connection => _connection;

        public static TasollerPlusTransportOpenResult Failed => default;

        public static TasollerPlusTransportOpenResult DeviceNotFound => new TasollerPlusTransportOpenResult(TasollerPlusConnectResult.DeviceNotFound, null);

        public static TasollerPlusTransportOpenResult MultipleDevicesFound => new TasollerPlusTransportOpenResult(TasollerPlusConnectResult.MultipleDevicesFound, null);

        public static TasollerPlusTransportOpenResult FromConnection(ITasollerPlusConnection? connection)
        {
            return connection is null ? Failed : new TasollerPlusTransportOpenResult(TasollerPlusConnectResult.Success, connection);
        }
    }
}
