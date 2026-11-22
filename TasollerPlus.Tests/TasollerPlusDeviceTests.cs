using System.Threading.Channels;

namespace TasollerPlus.Tests;

[TestClass]
public sealed class TasollerPlusDeviceTests
{
    private const int InputReportLength = 36;
    private const int ShortInputReportLength = InputReportLength - 1;
    private const int InputHeaderStartOffset = 0;
    private const int DigitalInputsReportOffset = 3;
    private const int FirstSliderReportOffset = 4;
    private const int FirstSliderIndex = 0;
    private const int AirBeamCount = 6;
    private const int BelowBottomAirBeamIndex = -1;
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
    private const byte ValidInputHeaderFirstByte = (byte)'D';
    private const byte InvalidInputHeaderFirstByte = (byte)'X';
    private const byte NoDigitalInputsReportBits = 0;
    private const byte Fn1PressedReportBit = 0b1000_0000;
    private const byte Fn2PressedReportBit = 0b0100_0000;
    private const byte BothFnButtonsReportBits = Fn1PressedReportBit | Fn2PressedReportBit;
    private const byte AllAirBeamsBlockedReportBits = 0b0011_1111;
    private const int LedReportLength = 114;
    private const int SliderLedColorCount = 31;
    private const int AirTowerLedColorCount = 3;
    private const string ExpectedFirstLedReportPrefixHex = "444C02123456";
    private const string ExpectedLatestLedReportPrefixHex = "444C026789AB";
    private static readonly RgbColor FirstSliderLedColor = new RgbColor(0x12, 0x34, 0x56);
    private static readonly RgbColor LatestSliderLedColor = new RgbColor(0x67, 0x89, 0xAB);
    private static readonly byte[] AirBeamReportBitsFromBottom = { 0b0010_0000, 0b0001_0000, 0b0000_1000, 0b0000_0100, 0b0000_0010, 0b0000_0001 };
    private static readonly TimeSpan InputAvailabilityTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LedReportAvailabilityTimeout = TimeSpan.FromSeconds(5);
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
        AssertUnsuccessfulConnect(new QueuedConnectionAdapter(), TasollerPlusConnectResult.DeviceNotFound);
    }

    [TestMethod]
    public void ConnectReturnsMultipleDevicesFoundWhenMoreThanOneDeviceMatches()
    {
        var adapter = new OpenResultAdapter(TasollerPlusTransportOpenResult.MultipleDevicesFound);
        AssertUnsuccessfulConnect(adapter, TasollerPlusConnectResult.MultipleDevicesFound);
    }

    [TestMethod]
    public void ConnectReturnsConnectionFailedWhenTheTransportCannotOpenTheDevice()
    {
        var adapter = new OpenResultAdapter(TasollerPlusTransportOpenResult.Failed);
        AssertUnsuccessfulConnect(adapter, TasollerPlusConnectResult.ConnectionFailed);
    }

    [TestMethod]
    public void ConnectReturnsConnectionFailedWhenTheConnectionIsMissing()
    {
        var adapter = new OpenResultAdapter(TasollerPlusTransportOpenResult.Opened);
        AssertUnsuccessfulConnect(adapter, TasollerPlusConnectResult.ConnectionFailed);
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
    [DataRow(NoDigitalInputsReportBits, false, false)]
    [DataRow(Fn1PressedReportBit, true, false)]
    [DataRow(Fn2PressedReportBit, false, true)]
    [DataRow(BothFnButtonsReportBits, true, true)]
    [DataRow(AllAirBeamsBlockedReportBits, false, false)]
    public void InputFrameReportsFnButtonStatesIndependently(byte digitalInputs, bool expectedFn1Pressed, bool expectedFn2Pressed)
    {
        var frame = ReadFrameWithDigitalInputs(digitalInputs);

        Assert.AreEqual(expectedFn1Pressed, frame.IsFn1Pressed);
        Assert.AreEqual(expectedFn2Pressed, frame.IsFn2Pressed);
    }

    [TestMethod]
    public void IsAirBlockedReportsTheStateOfEachBeamFromBottomToTop()
    {
        for (var blockedIndex = 0; blockedIndex < AirBeamReportBitsFromBottom.Length; blockedIndex++)
        {
            var digitalInputs = (byte)(BothFnButtonsReportBits | AirBeamReportBitsFromBottom[blockedIndex]);
            var frame = ReadFrameWithDigitalInputs(digitalInputs);

            for (var index = 0; index < AirBeamCount; index++)
            {
                Assert.AreEqual(index == blockedIndex, frame.IsAirBlocked(index), $"Unexpected AIR state at index {index} with beam {blockedIndex} blocked.");
            }
        }
    }

    [TestMethod]
    [DataRow(BelowBottomAirBeamIndex)]
    [DataRow(AirBeamCount)]
    public void IsAirBlockedRejectsAnIndexOutsideTheBeamRange(int index)
    {
        var frame = ReadFrameWithDigitalInputs(BothFnButtonsReportBits);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => frame.IsAirBlocked(index));
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
    public async Task TrySetLedColorsSendsTheRequestedColorsWhileWaitingForInput()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        using var timeout = new CancellationTokenSource(LedReportAvailabilityTimeout);
        await adapter.WaitingForInput.WaitAsync(timeout.Token);

        var slider = new RgbColor[SliderLedColorCount];
        var leftAir = new RgbColor[AirTowerLedColorCount];
        var rightAir = new RgbColor[AirTowerLedColorCount];
        slider[FirstSliderIndex] = FirstSliderLedColor;
        var expectedReport = CreateExpectedLedReport(ExpectedFirstLedReportPrefixHex);

        Assert.IsTrue(device.TrySetLedColors(slider, leftAir, rightAir), "The connected device did not accept the LED colors.");
        var actualReport = await adapter.ReadWrittenLedReportAsync(timeout.Token);
        CollectionAssert.AreEqual(expectedReport, actualReport);
    }

    [TestMethod]
    public async Task TrySetLedColorsSendsLatestPendingColorsAfterCurrentWrite()
    {
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue), async cancellationToken =>
        {
            if (firstWriteStarted.TrySetResult())
            {
                await finishFirstWrite.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return TasollerPlusTransferStatus.Succeeded;
        });
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        using var timeout = new CancellationTokenSource(LedReportAvailabilityTimeout);
        var slider = new RgbColor[SliderLedColorCount];
        var air = new RgbColor[AirTowerLedColorCount];
        slider[FirstSliderIndex] = FirstSliderLedColor;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        await firstWriteStarted.Task.WaitAsync(timeout.Token);

        slider[FirstSliderIndex] = default;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        slider[FirstSliderIndex] = LatestSliderLedColor;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        finishFirstWrite.SetResult();

        var firstReport = await adapter.ReadWrittenLedReportAsync(timeout.Token);
        var nextReport = await adapter.ReadWrittenLedReportAsync(timeout.Token);
        CollectionAssert.AreEqual(CreateExpectedLedReport(ExpectedFirstLedReportPrefixHex), firstReport);
        CollectionAssert.AreEqual(CreateExpectedLedReport(ExpectedLatestLedReportPrefixHex), nextReport, "The next write did not contain the most recent pending colors.");
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
    [DataRow(InputReportLength, InvalidInputHeaderFirstByte)]
    [DataRow(ShortInputReportLength, ValidInputHeaderFirstByte)]
    public void InputReceptionSkipsAnInvalidReport(int reportLength, byte headerFirstByte)
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        var firstFrame = WaitForLatestInput(device);

        var invalidReport = CreateInputReport(SecondReportSliderValue)[..reportLength];
        invalidReport[InputHeaderStartOffset] = headerFirstByte;
        adapter.EnqueueInputReport(invalidReport);
        adapter.EnqueueInputReport(CreateInputReport(ThirdReportSliderValue));

        var nextFrame = WaitForLatestSliderValue(device, ThirdReportSliderValue);
        Assert.AreEqual(TasollerPlusConnectionStatus.Connected, device.Status);
        Assert.AreEqual(firstFrame.SequenceNumber + ExpectedSequenceStep, nextFrame.SequenceNumber);
        AssertPendingSliderValues(device, device.InputBufferCapacity, FirstReportSliderValue, ThirdReportSliderValue);
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
    public async Task DisconnectKeepsDisconnectedStatusWhenItInterruptsLedSending()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue), async cancellationToken =>
        {
            var writeResult = new TaskCompletionSource<TasollerPlusTransferStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => writeResult.TrySetResult(TasollerPlusTransferStatus.Failed));
            writeStarted.SetResult();
            return await writeResult.Task.ConfigureAwait(false);
        });
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestInput(device);

        var slider = new RgbColor[SliderLedColorCount];
        var air = new RgbColor[AirTowerLedColorCount];
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        await writeStarted.Task.WaitAsync(LedReportAvailabilityTimeout);

        await Task.Run(device.Disconnect).WaitAsync(LedReportAvailabilityTimeout);

        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
        Assert.IsTrue(adapter.IsClosed);
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

    [TestMethod]
    [DataRow(true, TasollerPlusConnectionStatus.DeviceRemoved)]
    [DataRow(false, TasollerPlusConnectionStatus.LedSendError)]
    public void LedSendFailureClosesTheConnectionAndPreservesTheFailureReason(bool deviceRemoved, TasollerPlusConnectionStatus expectedStatus)
    {
        var writeStatus = deviceRemoved ? TasollerPlusTransferStatus.DeviceRemoved : TasollerPlusTransferStatus.Failed;
        var adapter = new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue), _ => ValueTask.FromResult(writeStatus));
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestInput(device);

        var slider = new RgbColor[SliderLedColorCount];
        var air = new RgbColor[AirTowerLedColorCount];
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));

        AssertConnectionEnded(device, adapter, expectedStatus);
        device.Disconnect();
        Assert.AreEqual(expectedStatus, device.Status);
    }

    private static void AssertUnsuccessfulConnect(ITasollerPlusTransport transport, TasollerPlusConnectResult expectedResult)
    {
        using var device = new TasollerPlusDevice(transport);
        Assert.AreEqual(expectedResult, device.Connect());
        Assert.AreEqual(TasollerPlusConnectionStatus.Disconnected, device.Status);
    }

    private static void AssertConnectionEnded(TasollerPlusDevice device, SingleDeviceAdapter adapter, TasollerPlusConnectionStatus expectedStatus)
    {
        SpinWait.SpinUntil(() => device.Status != TasollerPlusConnectionStatus.Connected, InputAvailabilityTimeout);
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

    private static TasollerPlusInputFrame ReadFrameWithDigitalInputs(byte digitalInputs)
    {
        var report = CreateInputReport(FirstReportSliderValue);
        report[DigitalInputsReportOffset] = digitalInputs;
        using var device = new TasollerPlusDevice(new SingleDeviceAdapter(report));
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        return WaitForLatestInput(device);
    }

    private static byte[] CreateExpectedLedReport(string prefixHex)
    {
        var report = new byte[LedReportLength];
        Convert.FromHexString(prefixHex).AsSpan().CopyTo(report);
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

    internal sealed class OpenResultAdapter : ITasollerPlusTransport
    {
        private readonly TasollerPlusTransportOpenResult _result;

        public OpenResultAdapter(TasollerPlusTransportOpenResult result)
        {
            _result = result;
        }

        public TasollerPlusTransportOpenResult Open(out ITasollerPlusConnection? connection)
        {
            connection = null;
            return _result;
        }
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
        private readonly Channel<byte[]> _writtenLedReports;
        private readonly TaskCompletionSource _waitingForInput;
        private readonly Func<CancellationToken, ValueTask<TasollerPlusTransferStatus>>? _completeWriteAsync;

        public SingleDeviceAdapter(byte[] firstReport, Func<CancellationToken, ValueTask<TasollerPlusTransferStatus>>? completeWriteAsync = null)
        {
            _completeWriteAsync = completeWriteAsync;

            _transfers = Channel.CreateUnbounded<(byte[]? Report, TasollerPlusReadTransferResult Result)>();
            _writtenLedReports = Channel.CreateUnbounded<byte[]>();
            _waitingForInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            EnqueueInputReport(firstReport);
        }

        public bool IsClosed => _transfers.Reader.Completion.IsCompleted;

        public Task WaitingForInput => _waitingForInput.Task;

        public void EnqueueInputReport(byte[] report)
        {
            EnqueueTransfer(report, new TasollerPlusReadTransferResult(report.Length));
        }

        public void RemoveDevice()
        {
            EnqueueTransfer(null, new TasollerPlusReadTransferResult(default, TasollerPlusTransferStatus.DeviceRemoved));
        }

        public void FailInputRead()
        {
            EnqueueTransfer(null, new TasollerPlusReadTransferResult(default, TasollerPlusTransferStatus.Failed));
        }

        public TasollerPlusTransportOpenResult Open(out ITasollerPlusConnection? connection)
        {
            connection = this;
            return TasollerPlusTransportOpenResult.Opened;
        }

        public async ValueTask<TasollerPlusReadTransferResult> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var pendingTransfer = _transfers.Reader.ReadAsync(cancellationToken);
            if (!pendingTransfer.IsCompleted)
            {
                _waitingForInput.TrySetResult();
            }

            var transfer = await pendingTransfer.ConfigureAwait(false);
            if (transfer.Report is { } report)
            {
                report.AsSpan().CopyTo(buffer.Span);
            }

            return transfer.Result;
        }

        public async ValueTask<TasollerPlusTransferStatus> WriteOutputAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = _completeWriteAsync is null ? TasollerPlusTransferStatus.Succeeded : await _completeWriteAsync(cancellationToken).ConfigureAwait(false);
            if (status != TasollerPlusTransferStatus.Succeeded)
            {
                return status;
            }

            var written = _writtenLedReports.Writer.TryWrite(report.ToArray());
            return written ? TasollerPlusTransferStatus.Succeeded : TasollerPlusTransferStatus.Failed;
        }

        public ValueTask<byte[]> ReadWrittenLedReportAsync(CancellationToken cancellationToken)
        {
            return _writtenLedReports.Reader.ReadAsync(cancellationToken);
        }

        public void Dispose()
        {
            _writtenLedReports.Writer.TryComplete();
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
