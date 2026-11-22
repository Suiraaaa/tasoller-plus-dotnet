namespace TasollerPlus
{
    internal interface ITasollerPlusTransport
    {
        // Opened requires an open connection. Return null for the other results.
        TasollerPlusTransportOpenResult Open(out ITasollerPlusConnection? connection);
    }
}
