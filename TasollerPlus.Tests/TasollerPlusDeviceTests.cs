using System.Threading.Channels;

namespace TasollerPlus.Tests;

[TestClass]
public sealed class TasollerPlusDeviceTests
{
    private const int InputReportLength = 36;
    private const int FirstSliderReportOffset = 4;
    private const int FirstSliderIndex = 0;
    private const byte FirstReportSliderValue = 0xA6;
    private const byte SecondReportSliderValue = 0x3C;
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
        var adapter = new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue));
        adapter.EnqueueInputReport(CreateInputReport(SecondReportSliderValue));
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        AssertLatestSliderValue(device, SecondReportSliderValue);

        var expectedSliderValues = new[] { FirstReportSliderValue, SecondReportSliderValue };
        var destination = new TasollerPlusInputFrame[expectedSliderValues.Length];
        var result = device.ReadPendingInputs(destination);

        Assert.AreEqual(expectedSliderValues.Length, result.Count, "ReadPendingInputs() did not return both received reports.");
        var actualSliderValues = destination.Select(frame => frame.GetSliderValue(FirstSliderIndex)).ToArray();
        CollectionAssert.AreEqual(expectedSliderValues, actualSliderValues);
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
