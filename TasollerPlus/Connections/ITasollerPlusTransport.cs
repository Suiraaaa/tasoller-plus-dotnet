namespace TasollerPlus.Connections
{
    internal interface ITasollerPlusTransport
    {
        TasollerPlusDeviceDetectionResult DetectDevice();

        // On success, the caller owns the returned connection.
        TasollerPlusTransportOpenResult Open();
    }
}
