using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;

using TasollerPlus.Connections;
using TasollerPlus.Tests.TestSupport;

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
    private const int LatestInputRetentionReportCount = 2;
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
    private const int FirstLedWrite = 1;
    private const int FirstLedResendWrite = FirstLedWrite + 1;
    private const int ObservedAutomaticResends = 2;
    private const string ExpectedFirstLedReportPrefixHex = "444C02123456";
    private const string ExpectedLatestLedReportPrefixHex = "444C026789AB";
    private static readonly RgbColor FirstSliderLedColor = new RgbColor(0x12, 0x34, 0x56);
    private static readonly RgbColor LatestSliderLedColor = new RgbColor(0x67, 0x89, 0xAB);
    private static readonly byte[] AirBeamReportBitsFromBottom = { 0b0010_0000, 0b0001_0000, 0b0000_1000, 0b0000_0100, 0b0000_0010, 0b0000_0001 };
    private static readonly TimeSpan InputAvailabilityTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LedReportAvailabilityTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumLedResendInterval = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan LedReplayObservationTime = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan HeldLedReplayObservationTime = TimeSpan.FromSeconds(2);
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
    public void ConnectReturnsConnectionFailedWhenTheTransportReturnsTheDefaultResult()
    {
        var adapter = new OpenResultAdapter(default);
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
    public async Task TrySetLedColorsAutomaticallyResendsTheReportEverySecond()
    {
        var writeTimestamps = new ConcurrentQueue<long>();
        var adapter = new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue), _ =>
        {
            writeTimestamps.Enqueue(Stopwatch.GetTimestamp());
            return ValueTask.FromResult(TasollerPlusTransferStatus.Succeeded);
        });
        using var device = new TasollerPlusDevice(adapter);
        using var timeout = new CancellationTokenSource(LedReportAvailabilityTimeout);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        var slider = new RgbColor[SliderLedColorCount];
        var air = new RgbColor[AirTowerLedColorCount];
        slider[FirstSliderIndex] = FirstSliderLedColor;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));

        var expectedReport = CreateExpectedLedReport(ExpectedFirstLedReportPrefixHex);
        var initialReport = await adapter.ReadWrittenLedReportAsync(timeout.Token);
        CollectionAssert.AreEqual(expectedReport, initialReport);
        Assert.IsTrue(writeTimestamps.TryDequeue(out var previousWriteTimestamp));

        for (var resendIndex = 0; resendIndex < ObservedAutomaticResends; resendIndex++)
        {
            try
            {
                var resentReport = await adapter.ReadWrittenLedReportAsync(timeout.Token);
                CollectionAssert.AreEqual(expectedReport, resentReport);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                Assert.Fail("The LED report was not resent after the successful write.");
            }

            Assert.IsTrue(writeTimestamps.TryDequeue(out var resendTimestamp));
            var elapsed = Stopwatch.GetElapsedTime(previousWriteTimestamp, resendTimestamp);
            Assert.IsTrue(elapsed >= MinimumLedResendInterval, $"The LED report was resent after only {elapsed.TotalMilliseconds:F0} milliseconds.");
            previousWriteTimestamp = resendTimestamp;
        }
    }

    [TestMethod]
    public async Task TrySetLedColorsSendsLatestPendingColorsAfterCurrentWrite()
    {
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = CreateAdapterWithBlockedLedWrite(FirstLedWrite, firstWriteStarted, finishFirstWrite.Task);
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
    public async Task TrySetLedColorsResendsTheLatestColorsAfterAnInFlightResend()
    {
        var resendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishResend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = CreateAdapterWithBlockedLedWrite(FirstLedResendWrite, resendStarted, finishResend.Task);
        using var device = new TasollerPlusDevice(adapter);
        using var timeout = new CancellationTokenSource(LedReportAvailabilityTimeout);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        var slider = new RgbColor[SliderLedColorCount];
        var air = new RgbColor[AirTowerLedColorCount];
        slider[FirstSliderIndex] = FirstSliderLedColor;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));

        var expectedOriginalReport = CreateExpectedLedReport(ExpectedFirstLedReportPrefixHex);
        CollectionAssert.AreEqual(expectedOriginalReport, await adapter.ReadWrittenLedReportAsync(timeout.Token));
        await resendStarted.Task.WaitAsync(timeout.Token);

        slider[FirstSliderIndex] = default;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        slider[FirstSliderIndex] = LatestSliderLedColor;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        finishResend.SetResult();

        var expectedLatestReport = CreateExpectedLedReport(ExpectedLatestLedReportPrefixHex);
        CollectionAssert.AreEqual(expectedOriginalReport, await adapter.ReadWrittenLedReportAsync(timeout.Token));
        CollectionAssert.AreEqual(expectedLatestReport, await adapter.ReadWrittenLedReportAsync(timeout.Token), "The next write did not contain the most recent pending colors.");
        CollectionAssert.AreEqual(expectedLatestReport, await adapter.ReadWrittenLedReportAsync(timeout.Token), "The next automatic resend did not use the latest successful colors.");
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
    public void TryGetLatestInputRetainsTheLatestFrameAfterReadingAllPendingInputs()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());

        var latestInput = WaitForLatestSliderValue(device, SecondReportSliderValue);
        var destination = new TasollerPlusInputFrame[LatestInputRetentionReportCount];
        Assert.AreEqual(LatestInputRetentionReportCount, device.ReadPendingInputs(destination).Count);
        Assert.AreEqual(NoPendingReportCount, device.ReadPendingInputs(destination).Count);

        Assert.IsTrue(device.TryGetLatestInput(out var frame), "The latest input was lost after reading all pending inputs.");
        Assert.AreEqual(latestInput.SequenceNumber, frame.SequenceNumber);
        Assert.AreEqual(SecondReportSliderValue, frame.GetSliderValue(FirstSliderIndex));
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
    public void DisposeClosesTheConnectionAndPreventsReconnecting()
    {
        var firstConnection = CreateAdapterWithReports(FirstReportSliderValue);
        var secondConnection = CreateAdapterWithReports(SecondReportSliderValue);
        using var device = new TasollerPlusDevice(new QueuedConnectionAdapter(firstConnection, secondConnection));
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestInput(device);

        device.Dispose();

        Assert.IsTrue(firstConnection.IsClosed);
        Assert.ThrowsExactly<ObjectDisposedException>(() => device.Connect());
    }

    [TestMethod]
    public void DisposeBeforeConnectingPreventsOpeningAConnection()
    {
        var adapter = CreateAdapterWithReports(FirstReportSliderValue);
        using var device = new TasollerPlusDevice(adapter);

        device.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => device.Connect());
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
    public void DeviceRemovalClearsTheUnreadBufferAndItsDroppedCount()
    {
        var firstConnection = CreateAdapterWithReports(FirstReportSliderValue, SecondReportSliderValue, ThirdReportSliderValue);
        var secondConnection = CreateAdapterWithReports(FourthReportSliderValue);
        using var device = new TasollerPlusDevice(new QueuedConnectionAdapter(firstConnection, secondConnection), inputBufferCapacity: OverflowTestCapacity);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, ThirdReportSliderValue);

        firstConnection.RemoveDevice();

        AssertConnectionEnded(device, firstConnection, TasollerPlusConnectionStatus.DeviceRemoved);

        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, FourthReportSliderValue);
        AssertPendingSliderValues(device, device.InputBufferCapacity, FourthReportSliderValue);
    }

    [TestMethod]
    public async Task ReconnectingAfterDeviceRemovalDoesNotReplayPendingLedColors()
    {
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstConnection = CreateAdapterWithBlockedLedWrite(FirstLedWrite, firstWriteStarted, Task.Delay(Timeout.InfiniteTimeSpan));
        var secondConnection = CreateAdapterWithReports(SecondReportSliderValue);
        using var device = new TasollerPlusDevice(new QueuedConnectionAdapter(firstConnection, secondConnection));
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, FirstReportSliderValue);
        using var timeout = new CancellationTokenSource(LedReportAvailabilityTimeout);

        var slider = new RgbColor[SliderLedColorCount];
        var air = new RgbColor[AirTowerLedColorCount];
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        await firstWriteStarted.Task.WaitAsync(timeout.Token);
        slider[FirstSliderIndex] = FirstSliderLedColor;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        firstConnection.RemoveDevice();
        AssertConnectionEnded(device, firstConnection, TasollerPlusConnectionStatus.DeviceRemoved);

        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, SecondReportSliderValue);
        var firstReportAfterReconnect = secondConnection.ReadWrittenLedReportAsync(timeout.Token).AsTask();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => firstReportAfterReconnect.WaitAsync(LedReplayObservationTime), "A pending LED report was sent before a new color was requested.");

        slider[FirstSliderIndex] = LatestSliderLedColor;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        var actualReport = await firstReportAfterReconnect;
        CollectionAssert.AreEqual(CreateExpectedLedReport(ExpectedLatestLedReportPrefixHex), actualReport);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EndingTheConnectionStopsAutomaticLedResendingAndClearsTheHeldColor(bool deviceRemoved)
    {
        var resendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishResend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstConnection = CreateAdapterWithBlockedLedWrite(FirstLedResendWrite, resendStarted, finishResend.Task);
        var secondConnection = CreateAdapterWithReports(SecondReportSliderValue);
        using var device = new TasollerPlusDevice(new QueuedConnectionAdapter(firstConnection, secondConnection));
        using var timeout = new CancellationTokenSource(LedReportAvailabilityTimeout);

        try
        {
            Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
            await firstConnection.WaitingForInput.WaitAsync(timeout.Token);
            var slider = new RgbColor[SliderLedColorCount];
            var air = new RgbColor[AirTowerLedColorCount];
            slider[FirstSliderIndex] = FirstSliderLedColor;
            Assert.IsTrue(device.TrySetLedColors(slider, air, air));
            CollectionAssert.AreEqual(CreateExpectedLedReport(ExpectedFirstLedReportPrefixHex), await firstConnection.ReadWrittenLedReportAsync(timeout.Token));
            await resendStarted.Task.WaitAsync(timeout.Token);

            if (deviceRemoved)
            {
                firstConnection.RemoveDevice();
            }
            else
            {
                try
                {
                    await Task.Run(device.Disconnect).WaitAsync(LedReportAvailabilityTimeout);
                }
                catch (TimeoutException)
                {
                    Assert.Fail("Disconnect did not cancel the automatic LED resend.");
                }
            }

            var expectedStatus = deviceRemoved ? TasollerPlusConnectionStatus.DeviceRemoved : TasollerPlusConnectionStatus.Disconnected;
            AssertConnectionEnded(device, firstConnection, expectedStatus);
            await Assert.ThrowsExactlyAsync<ChannelClosedException>(() => firstConnection.ReadWrittenLedReportAsync(timeout.Token).AsTask());

            timeout.CancelAfter(LedReportAvailabilityTimeout);
            Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
            await secondConnection.WaitingForInput.WaitAsync(timeout.Token);
            var firstReportAfterReconnect = secondConnection.ReadWrittenLedReportAsync(timeout.Token).AsTask();
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => firstReportAfterReconnect.WaitAsync(HeldLedReplayObservationTime), "A held LED color was resent after reconnecting.");

            slider[FirstSliderIndex] = LatestSliderLedColor;
            Assert.IsTrue(device.TrySetLedColors(slider, air, air));
            CollectionAssert.AreEqual(CreateExpectedLedReport(ExpectedLatestLedReportPrefixHex), await firstReportAfterReconnect);
        }
        finally
        {
            finishResend.TrySetResult();
        }
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

    [TestMethod]
    [DataRow(false, TasollerPlusConnectionStatus.LedSendError)]
    [DataRow(true, TasollerPlusConnectionStatus.DeviceRemoved)]
    public async Task AutomaticLedResendFailureEndsTheConnection(bool deviceRemoved, TasollerPlusConnectionStatus expectedStatus)
    {
        var failure = deviceRemoved ? TasollerPlusTransferStatus.DeviceRemoved : TasollerPlusTransferStatus.Failed;
        var writeCount = 0;
        var adapter = new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue), _ =>
        {
            var status = Interlocked.Increment(ref writeCount) == FirstLedWrite ? TasollerPlusTransferStatus.Succeeded : failure;
            return ValueTask.FromResult(status);
        });
        using var device = new TasollerPlusDevice(adapter);
        using var timeout = new CancellationTokenSource(LedReportAvailabilityTimeout);
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        await adapter.WaitingForInput.WaitAsync(timeout.Token);

        var slider = new RgbColor[SliderLedColorCount];
        var air = new RgbColor[AirTowerLedColorCount];
        slider[FirstSliderIndex] = FirstSliderLedColor;
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        CollectionAssert.AreEqual(CreateExpectedLedReport(ExpectedFirstLedReportPrefixHex), await adapter.ReadWrittenLedReportAsync(timeout.Token));

        AssertConnectionEnded(device, adapter, expectedStatus);
    }

    [TestMethod]
    public void ReconnectingSucceedsAfterLedSendThrowsAnIoException()
    {
        var firstConnection = new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue), _ => throw new IOException("The simulated LED write failed."));
        var secondConnection = CreateAdapterWithReports(SecondReportSliderValue);
        using var device = new TasollerPlusDevice(new QueuedConnectionAdapter(firstConnection, secondConnection));
        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, FirstReportSliderValue);

        var slider = new RgbColor[SliderLedColorCount];
        var air = new RgbColor[AirTowerLedColorCount];
        Assert.IsTrue(device.TrySetLedColors(slider, air, air));
        AssertConnectionEnded(device, firstConnection, TasollerPlusConnectionStatus.LedSendError);

        Assert.AreEqual(TasollerPlusConnectResult.Success, device.Connect());
        WaitForLatestSliderValue(device, SecondReportSliderValue);
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
        var result = device.ReadPendingInputs(destination);
        Assert.AreEqual(NoPendingReportCount, result.Count);
        Assert.AreEqual(NoDroppedReports, result.DroppedCount);
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

    private static SingleDeviceAdapter CreateAdapterWithBlockedLedWrite(int writeNumber, TaskCompletionSource writeStarted, Task finishWrite)
    {
        var writeCount = 0;
        return new SingleDeviceAdapter(CreateInputReport(FirstReportSliderValue), async cancellationToken =>
        {
            if (Interlocked.Increment(ref writeCount) == writeNumber)
            {
                writeStarted.SetResult();
                await finishWrite.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return TasollerPlusTransferStatus.Succeeded;
        });
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

        public TasollerPlusTransportOpenResult Open() => _result;
    }

    internal sealed class QueuedConnectionAdapter : ITasollerPlusTransport
    {
        private readonly Queue<ITasollerPlusConnection> _connections;

        public QueuedConnectionAdapter(params ITasollerPlusConnection[] connections)
        {
            _connections = new Queue<ITasollerPlusConnection>(connections);
        }

        public TasollerPlusTransportOpenResult Open()
        {
            return _connections.TryDequeue(out var connection) ? TasollerPlusTransportOpenResult.FromConnection(connection) : TasollerPlusTransportOpenResult.DeviceNotFound;
        }
    }
}
