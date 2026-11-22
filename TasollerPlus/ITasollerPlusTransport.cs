namespace TasollerPlus
{
    internal interface ITasollerPlusTransport
    {
        // The connection is present when the result is Opened.
        TasollerPlusTransportOpenResult Open(out ITasollerPlusConnection? connection);
    }
}
