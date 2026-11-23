namespace TasollerPlus.Connections
{
    internal interface ITasollerPlusTransport
    {
        // On success, the caller owns the returned connection.
        TasollerPlusTransportOpenResult Open();
    }
}
