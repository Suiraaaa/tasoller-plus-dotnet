using TasollerPlus.Connections;
using TasollerPlus.Connections.WinUsb;

namespace TasollerPlus.Tests.Connections.WinUsb;

[TestClass]
public sealed class TasollerPlusWinUsbConnectionTests
{
    private const string OpenedDeviceInstanceId = @"USB\VID_0E8F&PID_1231&MI_00\OPENED-DEVICE";
    private const int ErrorGeneralFailure = 31;
    private const int ErrorDevNotExist = 55;
    private const int ErrorSemaphoreTimeout = 121;
    private const int ErrorNoSuchDevice = 433;
    private const int ErrorDeviceNotConnected = 1167;
    private const int ErrorDeviceRemoved = 1617;

    [TestMethod]
    [DataRow(ErrorDevNotExist)]
    [DataRow(ErrorNoSuchDevice)]
    [DataRow(ErrorDeviceNotConnected)]
    [DataRow(ErrorDeviceRemoved)]
    public void RecognizesWin32CodesThatIndicateDeviceAbsence(int error)
    {
        var status = TasollerPlusWinUsbConnection.ClassifyTransferFailure(error, OpenedDeviceInstanceId, _ => throw new AssertFailedException("An explicit removal code does not require a device lookup."));

        Assert.AreEqual(TasollerPlusTransferStatus.DeviceRemoved, status);
    }

    [TestMethod]
    [DataRow(ErrorGeneralFailure, true)]
    [DataRow(ErrorGeneralFailure, false)]
    [DataRow(ErrorSemaphoreTimeout, false)]
    public void UsesTheOpenedDevicePresenceWhenTheTransferErrorIsUnspecified(int error, bool deviceAbsent)
    {
        var queriedInstanceId = default(string);
        var status = TasollerPlusWinUsbConnection.ClassifyTransferFailure(error, OpenedDeviceInstanceId, instanceId =>
        {
            queriedInstanceId = instanceId;
            return deviceAbsent;
        });

        Assert.AreEqual(deviceAbsent ? TasollerPlusTransferStatus.DeviceRemoved : TasollerPlusTransferStatus.Failed, status);
        Assert.AreEqual(OpenedDeviceInstanceId, queriedInstanceId);
    }
}
