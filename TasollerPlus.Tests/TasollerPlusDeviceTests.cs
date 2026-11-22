namespace TasollerPlus.Tests;

[TestClass]
public sealed class TasollerPlusDeviceTests
{
    private const int InputReportLength = 36;
    private const int FirstSliderReportOffset = 4;
    private const int FirstSliderIndex = 0;
    private const byte FirstSliderValue = 0xA6;
    private static readonly TimeSpan InputAvailabilityTimeout = TimeSpan.FromSeconds(5);
    private static ReadOnlySpan<byte> InputReportHeader => "DBT"u8;

    [TestMethod]
    public void TryGetLatestInputReturnsTheFirstReportAfterConnecting()
    {
        var report = new byte[InputReportLength];
        InputReportHeader.CopyTo(report);
        report[FirstSliderReportOffset] = FirstSliderValue;

        using var device = new TasollerPlusDevice(new SingleDeviceAdapter(report));
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        var frame = default(TasollerPlusInputFrame);
        var received = SpinWait.SpinUntil(() => device.TryGetLatestInput(out frame), InputAvailabilityTimeout);

        Assert.IsTrue(received, "TryGetLatestInput() did not return the first input report from the connected device.");
        Assert.AreEqual(FirstSliderValue, frame.GetSliderValue(FirstSliderIndex));
    }

    internal sealed class SingleDeviceAdapter : ITasollerPlusTransport, ITasollerPlusConnection
    {
        private readonly TaskCompletionSource<int> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private byte[]? _nextReport;

        public SingleDeviceAdapter(byte[] firstReport)
        {
            _nextReport = firstReport;
        }

        public TasollerPlusConnectResult Open(out ITasollerPlusConnection? connection)
        {
            connection = this;
            return TasollerPlusConnectResult.Success;
        }

        public ValueTask<int> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var report = Interlocked.Exchange(ref _nextReport, null);
            if (report is null)
            {
                return new ValueTask<int>(_closed.Task.WaitAsync(cancellationToken));
            }

            report.AsSpan().CopyTo(buffer.Span);
            return new ValueTask<int>(report.Length);
        }

        public void Dispose()
        {
            _closed.TrySetCanceled();
        }
    }
}
