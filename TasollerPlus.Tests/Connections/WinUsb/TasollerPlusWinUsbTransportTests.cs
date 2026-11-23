using TasollerPlus.Connections.WinUsb;
using TasollerPlus.Tests.TestSupport;

namespace TasollerPlus.Tests.Connections.WinUsb;

[TestClass]
public sealed class TasollerPlusWinUsbTransportTests
{
    private const string MatchingInstanceId = @"usb\vid_0e8f&pid_1231&mi_00\TARGET";
    private const string SecondMatchingInstanceId = @"USB\VID_0E8F&PID_1231\SECOND-DEVICE";
    private const string NonMatchingInstanceId = @"USB\VID_0E8F&PID_1231&MI_01\OTHER-INTERFACE";
    private const int InputReportLength = 36;
    private const int FirstSliderOffset = 4;
    private const int FirstSliderIndex = 0;
    private const byte ExpectedSliderValue = 0xA6;
    private const int ExpectedOpenAttempts = 1;
    private static readonly TimeSpan InputAvailabilityTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    public void DetectDeviceFindsTheOnlyMatchingWinUsbDeviceWithoutOpeningAConnection()
    {
        using var device = CreateEnumerationOnlyDevice(() => new[] { NonMatchingInstanceId, MatchingInstanceId });

        Assert.AreEqual(TasollerPlusDeviceDetectionResult.DeviceFound, device.DetectDevice());
        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
    }

    [TestMethod]
    public void DetectDeviceIgnoresNonMatchingWinUsbDevices()
    {
        using var device = CreateEnumerationOnlyDevice(() => new[] { NonMatchingInstanceId });

        Assert.AreEqual(TasollerPlusDeviceDetectionResult.DeviceNotFound, device.DetectDevice());
    }

    [TestMethod]
    public void DetectDeviceReportsMultipleMatchingWinUsbDevices()
    {
        using var device = CreateEnumerationOnlyDevice(() => new[] { MatchingInstanceId, SecondMatchingInstanceId });

        Assert.AreEqual(TasollerPlusDeviceDetectionResult.MultipleDevicesFound, device.DetectDevice());
    }

    [TestMethod]
    public void DetectDeviceDistinguishesAnEmptyDeviceListFromEnumerationFailure()
    {
        var enumerationFails = false;
        using var device = CreateEnumerationOnlyDevice(() => enumerationFails ? null : Array.Empty<string>());

        Assert.AreEqual(TasollerPlusDeviceDetectionResult.DeviceNotFound, device.DetectDevice());

        enumerationFails = true;

        Assert.AreEqual(TasollerPlusDeviceDetectionResult.DetectionFailed, device.DetectDevice());
    }

    [TestMethod]
    public void DetectDeviceRejectsADisposedInstanceBeforeEnumeratingDevices()
    {
        using var device = CreateEnumerationOnlyDevice(() => throw new AssertFailedException("Device enumeration should not run after disposal."));
        device.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => device.DetectDevice());
    }

    [TestMethod]
    public void DetectDevicePreservesAnExistingConnection()
    {
        var report = new byte[InputReportLength];
        "DBT"u8.CopyTo(report);
        var connection = new SingleDeviceAdapter(report);
        var instanceIds = new[] { MatchingInstanceId };
        var transport = new TasollerPlusWinUsbTransport(() => instanceIds, _ => connection);
        using var device = new TasollerPlusDevice(transport);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        instanceIds = Array.Empty<string>();

        Assert.AreEqual(TasollerPlusDeviceDetectionResult.DeviceNotFound, device.DetectDevice());
        Assert.AreEqual(TasollerPlusConnectionStatus.Connected, device.Status);
    }

    [TestMethod]
    public void ConnectChecksTheCurrentDeviceListAfterDetection()
    {
        var instanceIds = new[] { MatchingInstanceId };
        using var device = CreateEnumerationOnlyDevice(() => instanceIds);
        Assert.AreEqual(TasollerPlusDeviceDetectionResult.DeviceFound, device.DetectDevice());

        instanceIds = Array.Empty<string>();

        Assert.AreEqual(TasollerPlusConnectResult.DeviceNotFound, device.Connect());
    }

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
        var instanceIds = new[] { MatchingInstanceId, SecondMatchingInstanceId };

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
        var connection = new SingleDeviceAdapter(report);
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

    private static TasollerPlusDevice CreateEnumerationOnlyDevice(Func<string[]?> readPresentDeviceInstanceIds)
    {
        var transport = new TasollerPlusWinUsbTransport(readPresentDeviceInstanceIds, _ => throw new AssertFailedException("The WinUSB connection opener should not be called."));
        return new TasollerPlusDevice(transport);
    }

    private static void AssertConnectResult(string[]? instanceIds, TasollerPlusConnectResult expected)
    {
        using var device = CreateEnumerationOnlyDevice(() => instanceIds);

        Assert.AreEqual(expected, device.Connect());
        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
    }
}
