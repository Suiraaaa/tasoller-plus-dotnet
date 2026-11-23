namespace TasollerPlus.Tests;

[TestClass]
public sealed class TasollerPlusWinUsbTransportTests
{
    [TestMethod]
    public void ConnectReportsDeviceNotFoundWhenNoWinUsbDeviceMatches()
    {
        var instanceIds = new[]
        {
            @"USB\VID_0E8E&PID_1231&MI_00\OTHER-VENDOR",
            @"USB\VID_0E8F&PID_1232&MI_00\OTHER-PRODUCT",
            @"USB\VID_0E8F&PID_1231&MI_01\OTHER-INTERFACE"
        };

        AssertConnectResult(instanceIds, TasollerPlusConnectResult.DeviceNotFound);
    }

    [TestMethod]
    public void ConnectReportsMultipleDevicesForTwoMatchingWinUsbDeviceInstances()
    {
        var instanceIds = new[]
        {
            @"usb\vid_0e8f&pid_1231&mi_00\FIRST-DEVICE",
            @"USB\VID_0E8F&PID_1231\SECOND-DEVICE"
        };

        AssertConnectResult(instanceIds, TasollerPlusConnectResult.MultipleDevicesFound);
    }

    [TestMethod]
    public void ConnectReportsConnectionFailedWhenWinUsbEnumerationFails()
    {
        AssertConnectResult(null, TasollerPlusConnectResult.ConnectionFailed);
    }

    private static void AssertConnectResult(string[]? instanceIds, TasollerPlusConnectResult expected)
    {
        using var device = new TasollerPlusDevice(new TasollerPlusWinUsbTransport(() => instanceIds));

        Assert.AreEqual(expected, device.Connect());
        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
    }
}
