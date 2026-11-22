namespace TasollerPlus
{
    internal interface ITasollerPlusTransport
    {
        TasollerPlusConnectResult Open(out ITasollerPlusConnection? connection);
    }
}
