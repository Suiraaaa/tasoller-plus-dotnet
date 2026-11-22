using System.Threading.Channels;

namespace TasollerPlus.Tests;

[TestClass]
public sealed class TasollerPlusDeviceTests
{
    private const int InputReportLength = 36;
    private const int FirstSliderReportOffset = 4;
    private const int FirstSliderIndex = 0;
    private const int OverflowTestCapacity = 2;
    private const int OverflowTestReportCount = 3;
    private const int SameTimestampReportCount = 2;
    private const long SharedReceiveTimestamp = 123456;
    private const long ExpectedSequenceStep = 1;
    private const long ExpectedDroppedCount = 2;
    private const long NoDroppedReports = 0;
    private const byte FirstReportSliderValue = 0xA6;
    private const byte SecondReportSliderValue = 0x3C;
    private const byte ThirdReportSliderValue = 0xE1;
    private const byte FourthReportSliderValue = 0x78;
    private static readonly TimeSpan InputAvailabilityTimeout = TimeSpan.FromSeconds(5);
    private static ReadOnlySpan<byte> InputReportHeader => "DBT"u8;

    [TestMethod]
    public void TryGetLatestInputReturnsTheFirstReportAfterConnecting()
    {
        var report = CreateInputReport(FirstReportSliderValue);
        using var device = new TasollerPlusDevice(new SingleDeviceAdapter(report));
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        var frame = WaitForLatestInput(device);
        Assert.AreEqual(FirstReportSliderValue, frame.GetSliderValue(FirstSliderIndex));
    }

    [TestMethod]
    public void TryGetLatestInputReturnsTheSecondReportWhenItArrives()
    {
        var adapter = new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue));
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        var firstFrame = WaitForLatestInput(device);
        Assert.AreEqual(FirstReportSliderValue, firstFrame.GetSliderValue(FirstSliderIndex));

        adapter.EnqueueInputReport(CreateInputReport(SecondReportSliderValue));
        AssertLatestSliderValue(device, SecondReportSliderValue);
    }

    [TestMethod]
    public void ReadPendingInputsReturnsReportsInReceiveOrder()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        AssertLatestSliderValue(device, SecondReportSliderValue);

        var expectedSliderValues = new[] { FirstReportSliderValue, SecondReportSliderValue };
        AssertPendingSliderValues(device, expectedSliderValues.Length, expectedSliderValues);
    }

    [TestMethod]
    public void ReadPendingInputsDropsTheOldestReportWhenTheBufferOverflows()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue, ThirdReportSliderValue);
        using var device = new TasollerPlusDevice(adapter, inputBufferCapacity: OverflowTestCapacity);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        AssertLatestSliderValue(device, ThirdReportSliderValue);

        AssertPendingSliderValues(device, OverflowTestReportCount, SecondReportSliderValue, ThirdReportSliderValue);
    }

    [TestMethod]
    public void ReadPendingInputsReportsDropsSinceThePreviousRead()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue, ThirdReportSliderValue, FourthReportSliderValue);
        using var device = new TasollerPlusDevice(adapter, inputBufferCapacity: OverflowTestCapacity);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        AssertLatestSliderValue(device, FourthReportSliderValue);

        var destination = new TasollerPlusInputFrame[OverflowTestCapacity];
        var firstRead = device.ReadPendingInputs(destination);
        Assert.AreEqual(ExpectedDroppedCount, firstRead.DroppedCount);

        var nextRead = device.ReadPendingInputs(destination);
        Assert.AreEqual(NoDroppedReports, nextRead.DroppedCount);
    }

    [TestMethod]
    public void ReadPendingInputsReturnsConsecutiveSequenceNumbersForReportsWithTheSameTimestamp()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue);
        using var device = new TasollerPlusDevice(adapter, getTimestamp: () => SharedReceiveTimestamp);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        AssertLatestSliderValue(device, SecondReportSliderValue);

        var destination = new TasollerPlusInputFrame[SameTimestampReportCount];
        var result = device.ReadPendingInputs(destination);
        Assert.AreEqual(destination.Length, result.Count);

        var first = destination.First();
        var second = destination.Last();
        Assert.AreEqual(ExpectedSequenceStep, second.SequenceNumber - first.SequenceNumber);
        Assert.AreEqual(SharedReceiveTimestamp, first.ReceivedTimestamp);
        Assert.AreEqual(SharedReceiveTimestamp, second.ReceivedTimestamp);
    }

    private static SingleDeviceAdapter CreateAdapterWithReports(byte firstSliderValue, params byte[] subsequentSliderValues)
    {
        var adapter = new SingleDeviceAdapter(CreateInputReport(firstSliderValue));
        foreach (var sliderValue in subsequentSliderValues)
        {
            adapter.EnqueueInputReport(CreateInputReport(sliderValue));
        }

        return adapter;
    }

    private static byte[] CreateInputReport(byte firstSliderValue)
    {
        var report = new byte[InputReportLength];
        InputReportHeader.CopyTo(report);
        report[FirstSliderReportOffset] = firstSliderValue;
        return report;
    }

    private static TasollerPlusInputFrame WaitForLatestInput(TasollerPlusDevice device)
    {
        var frame = default(TasollerPlusInputFrame);
        var received = SpinWait.SpinUntil(() => device.TryGetLatestInput(out frame), InputAvailabilityTimeout);
        Assert.IsTrue(received, "TryGetLatestInput() did not return the first input report from the connected device.");
        return frame;
    }

    private static void AssertLatestSliderValue(TasollerPlusDevice device, byte expectedSliderValue)
    {
        var actualSliderValue = default(byte?);
        SpinWait.SpinUntil(() =>
        {
            if (device.TryGetLatestInput(out var frame))
            {
                actualSliderValue = frame.GetSliderValue(FirstSliderIndex);
            }

            return actualSliderValue == expectedSliderValue;
        }, InputAvailabilityTimeout);

        Assert.AreEqual<byte?>(expectedSliderValue, actualSliderValue, "TryGetLatestInput() did not return the expected slider value before the timeout.");
    }

    private static void AssertPendingSliderValues(TasollerPlusDevice device, int destinationLength, params byte[] expectedSliderValues)
    {
        var destination = new TasollerPlusInputFrame[destinationLength];
        var result = device.ReadPendingInputs(destination);
        Assert.AreEqual(expectedSliderValues.Length, result.Count, "ReadPendingInputs() returned an unexpected number of reports.");

        var actualSliderValues = destination.Take(result.Count).Select(frame => frame.GetSliderValue(FirstSliderIndex)).ToArray();
        CollectionAssert.AreEqual(expectedSliderValues, actualSliderValues);
    }

    internal sealed class SingleDeviceAdapter : ITasollerPlusTransport, ITasollerPlusConnection
    {
        private readonly Channel<byte[]> _reports;

        public SingleDeviceAdapter(byte[] firstReport)
        {
            _reports = Channel.CreateUnbounded<byte[]>();

            EnqueueInputReport(firstReport);
        }

        public void EnqueueInputReport(byte[] report)
        {
            if (!_reports.Writer.TryWrite(report))
            {
                throw new InvalidOperationException("The test device is closed.");
            }
        }

        public TasollerPlusConnectResult Open(out ITasollerPlusConnection? connection)
        {
            connection = this;
            return TasollerPlusConnectResult.Success;
        }

        public async ValueTask<int> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var report = await _reports.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            report.AsSpan().CopyTo(buffer.Span);
            return report.Length;
        }

        public void Dispose()
        {
            _reports.Writer.TryComplete();
        }
    }
}
