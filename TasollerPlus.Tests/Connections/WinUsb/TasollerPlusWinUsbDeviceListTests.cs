using TasollerPlus.Connections.WinUsb;

namespace TasollerPlus.Tests.Connections.WinUsb;

[TestClass]
public sealed class TasollerPlusWinUsbDeviceListTests
{
    private const string FirstDevice = "first-device";
    private const string SecondDevice = "second-device";
    private const string DeviceList = FirstDevice + "\0" + SecondDevice + "\0\0";
    private const uint ConfigurationSuccess = 0x00000000;
    private const uint ConfigurationBufferTooSmall = 0x0000001A;
    private const uint ConfigurationInvalidData = 0x0000001F;
    private const uint InitialCharacterCount = 1;
    private const int NoCalls = 0;
    private const int SingleCall = 1;
    private const int ExpectedMaximumAttempts = 3;

    [TestMethod]
    public void RetriesWithTheNewSizeWhenTheListGrows()
    {
        var characterCount = InitialCharacterCount;

        var result = TasollerPlusWinUsbDeviceList.ReadMultiStringList(() => (ConfigurationSuccess, characterCount), buffer =>
        {
            if (buffer.Length < DeviceList.Length)
            {
                characterCount = (uint)DeviceList.Length;
                return ConfigurationBufferTooSmall;
            }

            DeviceList.AsSpan().CopyTo(buffer);
            return ConfigurationSuccess;
        });

        CollectionAssert.AreEqual(new[] { FirstDevice, SecondDevice }, result);
    }

    [TestMethod]
    public void ReturnsFailureWhenTheListKeepsGrowing()
    {
        var bufferReads = 0;

        var result = TasollerPlusWinUsbDeviceList.ReadMultiStringList(() => (ConfigurationSuccess, InitialCharacterCount), _ =>
        {
            bufferReads++;
            if (bufferReads > ExpectedMaximumAttempts)
            {
                Assert.Fail("The buffer size was retried too many times.");
            }

            return ConfigurationBufferTooSmall;
        });

        Assert.IsNull(result);
        Assert.AreEqual(ExpectedMaximumAttempts, bufferReads);
    }

    [TestMethod]
    [DataRow(ConfigurationInvalidData, ConfigurationSuccess, NoCalls)]
    [DataRow(ConfigurationSuccess, ConfigurationInvalidData, SingleCall)]
    public void ReturnsFailureWithoutRetryingOtherErrors(uint sizeResult, uint listResult, int expectedBufferReads)
    {
        var sizeQueries = 0;
        var bufferReads = 0;

        var result = TasollerPlusWinUsbDeviceList.ReadMultiStringList(() =>
        {
            sizeQueries++;
            Assert.AreEqual(SingleCall, sizeQueries, "The size query was retried after a non-buffer error.");
            return (sizeResult, InitialCharacterCount);
        }, _ =>
        {
            bufferReads++;
            return listResult;
        });

        Assert.IsNull(result);
        Assert.AreEqual(expectedBufferReads, bufferReads);
    }

    [TestMethod]
    public void ReturnsAnEmptyListWhenEnumerationSucceedsWithoutEntries()
    {
        var result = TasollerPlusWinUsbDeviceList.ReadMultiStringList(() => (ConfigurationSuccess, InitialCharacterCount), _ => ConfigurationSuccess);

        CollectionAssert.AreEqual(Array.Empty<string>(), result);
    }
}
