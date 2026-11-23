namespace TasollerPlus.Tests;

[TestClass]
public sealed class TasollerPlusWinUsbTransportTests
{
    private const string MatchingInstanceId = @"usb\vid_0e8f&pid_1231&mi_00\TARGET";
    private const string NonMatchingInstanceId = @"USB\VID_0E8F&PID_1231&MI_01\OTHER-INTERFACE";
    private const int InputReportLength = 36;
    private const int FirstSliderOffset = 4;
    private const int FirstSliderIndex = 0;
    private const byte ExpectedSliderValue = 0xA6;
    private const int ExpectedOpenAttempts = 1;
    private static readonly TimeSpan InputAvailabilityTimeout = TimeSpan.FromSeconds(5);

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

    [TestMethod]
    public async Task ConnectUsesTheOnlyMatchingWinUsbInstance()
    {
        var report = new byte[InputReportLength];
        "DBT"u8.CopyTo(report);
        report[FirstSliderOffset] = ExpectedSliderValue;
        var connection = new TasollerPlusDeviceTests.SingleDeviceAdapter(report);
        var instanceIds = new[] { NonMatchingInstanceId, MatchingInstanceId };
        var transport = new TasollerPlusWinUsbTransport(() => instanceIds, instanceId =>
        {
            Assert.AreEqual(MatchingInstanceId, instanceId);
            return connection;
        });
        using var device = new TasollerPlusDevice(transport);

        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        await connection.WaitingForInput.WaitAsync(InputAvailabilityTimeout);
        Assert.IsTrue(device.TryGetLatestInput(out var frame));
        Assert.AreEqual(ExpectedSliderValue, frame.GetSliderValue(FirstSliderIndex));

        device.Disconnect();
        Assert.IsTrue(connection.IsClosed);
    }

    [TestMethod]
    public void ConnectReportsFailureWhenOpeningTheMatchingWinUsbInstanceFails()
    {
        var openAttempts = 0;
        var transport = new TasollerPlusWinUsbTransport(() => new[] { MatchingInstanceId }, _ =>
        {
            openAttempts++;
            return null;
        });
        using var device = new TasollerPlusDevice(transport);

        Assert.AreEqual(TasollerPlusConnectResult.ConnectionFailed, device.Connect());
        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
        Assert.AreEqual(ExpectedOpenAttempts, openAttempts);
    }

    private static void AssertConnectResult(string[]? instanceIds, TasollerPlusConnectResult expected)
    {
        var transport = new TasollerPlusWinUsbTransport(() => instanceIds, _ => throw new AssertFailedException("The connection opener was called without a unique matching device."));
        using var device = new TasollerPlusDevice(transport);

        Assert.AreEqual(expected, device.Connect());
        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
    }
}
