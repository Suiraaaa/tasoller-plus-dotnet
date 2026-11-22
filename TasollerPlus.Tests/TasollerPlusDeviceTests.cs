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
    private const int NoPendingReportCount = 0;
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
    public void ConnectReturnsAlreadyConnectedWhenAConnectionIsActive()
    {
        var connection = CreateAdapterWithReports(FirstReportSliderValue);
        using var device = new TasollerPlusDevice(new QueuedConnectionAdapter(connection));
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, FirstReportSliderValue);

        Assert.AreEqual(TasollerPlusConnectResult.AlreadyConnected, device.Connect());

        connection.EnqueueInputReport(CreateInputReport(SecondReportSliderValue));
        WaitForLatestSliderValue(device, SecondReportSliderValue);
    }

    [TestMethod]
    public void ConnectReturnsDeviceNotFoundWhenNoDeviceIsPresent()
    {
        using var device = new TasollerPlusDevice(new QueuedConnectionAdapter());

        Assert.AreEqual(TasollerPlusConnectResult.DeviceNotFound, device.Connect());
        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
    }

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
        WaitForLatestSliderValue(device, SecondReportSliderValue);
    }

    [TestMethod]
    public void ReadPendingInputsReturnsReportsInReceiveOrder()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        WaitForLatestSliderValue(device, SecondReportSliderValue);

        var expectedSliderValues = new[] { FirstReportSliderValue, SecondReportSliderValue };
        AssertPendingSliderValues(device, expectedSliderValues.Length, expectedSliderValues);
    }

    [TestMethod]
    public void ReadPendingInputsDropsTheOldestReportWhenTheBufferOverflows()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue, ThirdReportSliderValue);
        using var device = new TasollerPlusDevice(adapter, inputBufferCapacity: OverflowTestCapacity);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        WaitForLatestSliderValue(device, ThirdReportSliderValue);

        AssertPendingSliderValues(device, OverflowTestReportCount, SecondReportSliderValue, ThirdReportSliderValue);
    }

    [TestMethod]
    public void ReadPendingInputsReportsDropsSinceThePreviousRead()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue, ThirdReportSliderValue, FourthReportSliderValue);
        using var device = new TasollerPlusDevice(adapter, inputBufferCapacity: OverflowTestCapacity);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        WaitForLatestSliderValue(device, FourthReportSliderValue);

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

        WaitForLatestSliderValue(device, SecondReportSliderValue);

        var destination = new TasollerPlusInputFrame[SameTimestampReportCount];
        var result = device.ReadPendingInputs(destination);
        Assert.AreEqual(destination.Length, result.Count);

        var first = destination.First();
        var second = destination.Last();
        Assert.AreEqual(ExpectedSequenceStep, second.SequenceNumber - first.SequenceNumber);
        Assert.AreEqual(SharedReceiveTimestamp, first.ReceivedTimestamp);
        Assert.AreEqual(SharedReceiveTimestamp, second.ReceivedTimestamp);
    }

    [TestMethod]
    public void DisconnectClosesTheConnectionAndClearsInputs()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, FirstReportSliderValue);

        device.Disconnect();

        Assert.IsFalse(device.TryGetLatestInput(out _), "The latest input was still available after Disconnect().");

        var destination = new TasollerPlusInputFrame[device.InputBufferCapacity];
        Assert.AreEqual(NoPendingReportCount, device.ReadPendingInputs(destination).Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => adapter.EnqueueInputReport(CreateInputReport(SecondReportSliderValue)));
    }

    [TestMethod]
    public void ReconnectingContinuesInputSequenceNumbers()
    {
        var firstConnection = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue);
        var secondConnection = CreateAdapterWithReports(ThirdReportSliderValue);
        using var device = new TasollerPlusDevice(new QueuedConnectionAdapter(firstConnection, secondConnection));
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        var lastFrameBeforeDisconnect = WaitForLatestSliderValue(device, SecondReportSliderValue);

        device.Disconnect();
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        var firstFrameAfterReconnect = WaitForLatestInput(device);
        Assert.AreEqual(ThirdReportSliderValue, firstFrameAfterReconnect.GetSliderValue(FirstSliderIndex));
        Assert.AreEqual(ExpectedSequenceStep, firstFrameAfterReconnect.SequenceNumber - lastFrameBeforeDisconnect.SequenceNumber);
    }

    [TestMethod]
    public void DeviceRemovalClosesTheConnectionAndPreservesTheRemovalStatus()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, FirstReportSliderValue);
        Assert.AreEqual(TasollerPlusConnectionStatus.Connected, device.Status);

        adapter.RemoveDevice();

        AssertConnectionEnded(device, adapter, TasollerPlusConnectionStatus.DeviceRemoved);

        device.Disconnect();
        Assert.AreEqual(TasollerPlusConnectionStatus.DeviceRemoved, device.Status);
    }

    [TestMethod]
    public void InputReceiveFailureClosesTheConnectionAndPreservesTheErrorStatus()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, FirstReportSliderValue);

        adapter.FailInputRead();

        AssertConnectionEnded(device, adapter, TasollerPlusConnectionStatus.InputReceiveError);

        device.Disconnect();
        Assert.AreEqual(TasollerPlusConnectionStatus.InputReceiveError, device.Status);
    }

    private static void AssertConnectionEnded(TasollerPlusDevice device, SingleDeviceAdapter adapter, TasollerPlusConnectionStatus expectedStatus)
    {
        SpinWait.SpinUntil(() => device.Status == expectedStatus, InputAvailabilityTimeout);
        Assert.AreEqual(expectedStatus, device.Status);
        Assert.IsFalse(device.TryGetLatestInput(out _));

        var destination = new TasollerPlusInputFrame[device.InputBufferCapacity];
        Assert.AreEqual(NoPendingReportCount, device.ReadPendingInputs(destination).Count);
        Assert.IsTrue(SpinWait.SpinUntil(() => adapter.IsClosed, InputAvailabilityTimeout), "The connection remained open after reporting an end status.");
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

    private static TasollerPlusInputFrame WaitForLatestSliderValue(TasollerPlusDevice device, byte expectedSliderValue)
    {
        var latestFrame = default(TasollerPlusInputFrame?);
        SpinWait.SpinUntil(() =>
        {
            if (device.TryGetLatestInput(out var frame))
            {
                latestFrame = frame;
            }

            return latestFrame?.GetSliderValue(FirstSliderIndex) == expectedSliderValue;
        }, InputAvailabilityTimeout);

        Assert.AreEqual<byte?>(expectedSliderValue, latestFrame?.GetSliderValue(FirstSliderIndex), "TryGetLatestInput() did not return the expected slider value before the timeout.");
        return latestFrame.GetValueOrDefault();
    }

    private static void AssertPendingSliderValues(TasollerPlusDevice device, int destinationLength, params byte[] expectedSliderValues)
    {
        var destination = new TasollerPlusInputFrame[destinationLength];
        var result = device.ReadPendingInputs(destination);
        Assert.AreEqual(expectedSliderValues.Length, result.Count, "ReadPendingInputs() returned an unexpected number of reports.");

        var actualSliderValues = destination.Take(result.Count).Select(frame => frame.GetSliderValue(FirstSliderIndex)).ToArray();
        CollectionAssert.AreEqual(expectedSliderValues, actualSliderValues);
    }

    internal sealed class QueuedConnectionAdapter : ITasollerPlusTransport
    {
        private readonly Queue<ITasollerPlusConnection> _connections;

        public QueuedConnectionAdapter(params ITasollerPlusConnection[] connections)
        {
            _connections = new Queue<ITasollerPlusConnection>(connections);
        }

        public TasollerPlusTransportOpenResult Open(out ITasollerPlusConnection? connection)
        {
            return _connections.TryDequeue(out connection) ? TasollerPlusTransportOpenResult.Opened : TasollerPlusTransportOpenResult.DeviceNotFound;
        }
    }

    internal sealed class SingleDeviceAdapter : ITasollerPlusTransport, ITasollerPlusConnection
    {
        private readonly Channel<(byte[]? Report, TasollerPlusReadTransferResult Result)> _transfers;

        public SingleDeviceAdapter(byte[] firstReport)
        {
            _transfers = Channel.CreateUnbounded<(byte[]? Report, TasollerPlusReadTransferResult Result)>();

            EnqueueInputReport(firstReport);
        }

        public bool IsClosed => _transfers.Reader.Completion.IsCompleted;

        public void EnqueueInputReport(byte[] report)
        {
            EnqueueTransfer(report, new TasollerPlusReadTransferResult(report.Length));
        }

        public void RemoveDevice()
        {
            EnqueueTransfer(null, new TasollerPlusReadTransferResult(default, TasollerPlusReadTransferStatus.DeviceRemoved));
        }

        public void FailInputRead()
        {
            EnqueueTransfer(null, new TasollerPlusReadTransferResult(default, TasollerPlusReadTransferStatus.Failed));
        }

        public TasollerPlusTransportOpenResult Open(out ITasollerPlusConnection? connection)
        {
            connection = this;
            return TasollerPlusTransportOpenResult.Opened;
        }

        public async ValueTask<TasollerPlusReadTransferResult> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var transfer = await _transfers.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (transfer.Report is { } report)
            {
                report.AsSpan().CopyTo(buffer.Span);
            }

            return transfer.Result;
        }

        public void Dispose()
        {
            _transfers.Writer.TryComplete();
        }

        private void EnqueueTransfer(byte[]? report, TasollerPlusReadTransferResult result)
        {
            if (!_transfers.Writer.TryWrite((report, result)))
            {
                throw new InvalidOperationException("The test device is closed.");
            }
        }
    }
}
