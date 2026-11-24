using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using TasollerPlus.Connections;
using TasollerPlus.Connections.WinUsb;

namespace TasollerPlus.Tests.Connections.WinUsb;

[TestClass]
[OSCondition(OperatingSystems.Windows)]
[SupportedOSPlatform("windows")]
public sealed class TasollerPlusWinUsbTransferTests
{
    private const string OpenedDeviceInstanceId = @"USB\VID_0E8F&PID_1231&MI_00\OPENED-DEVICE";
    private const byte InputPipeId = 0x84;
    private const byte LedOutputPipeId = 0x03;
    private const int BufferLength = 4;
    private const int ShortTransferLength = 2;
    private const int OverreportedReadLength = BufferLength + 1;
    private const int NoBytesTransferred = 0;
    private const int SingleCancellationRequest = 1;
    private const int PendingPinCount = 1;
    private const int ReleasedPinCount = 0;
    private const int ErrorSuccess = 0;
    private const int ErrorGeneralFailure = 31;
    private const int ErrorDevNotExist = 55;
    private const int ErrorSemaphoreTimeout = 121;
    private const int ErrorNoSuchDevice = 433;
    private const int ErrorOperationAborted = 995;
    private const int ErrorIoIncomplete = 996;
    private const int ErrorIoPending = 997;
    private const int ErrorDeviceNotConnected = 1167;
    private const int ErrorDeviceRemoved = 1617;
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow(ErrorSuccess, false, BufferLength)]
    [DataRow(ErrorDeviceNotConnected, true, NoBytesTransferred)]
    public async Task ReadReturnsAnIncompleteOperationUntilWindowsSignals(int completionError, bool deviceRemoved, int expectedByteCount)
    {
        var pipes = new TestPipeOperations(InputPipeId, ErrorIoPending, BufferLength);
        using var connection = new TasollerPlusWinUsbConnection(string.Empty, pipes);
        var buffer = new byte[BufferLength];

        var transfer = connection.ReadInputAsync(buffer, CancellationToken.None).AsTask();
        Assert.IsFalse(transfer.IsCompleted, "The read must return before the native operation completes.");

        pipes.Complete(completionError);
        var result = await transfer.WaitAsync(CompletionTimeout);
        var expectedStatus = deviceRemoved ? TasollerPlusTransferStatus.DeviceRemoved : TasollerPlusTransferStatus.Succeeded;
        Assert.AreEqual(expectedStatus, result.Status);
        Assert.AreEqual(expectedByteCount, result.BytesRead);
    }

    [TestMethod]
    public async Task WriteReturnsAnIncompleteOperationUntilWindowsSignals()
    {
        var pipes = new TestPipeOperations(LedOutputPipeId, ErrorIoPending, BufferLength);
        using var connection = new TasollerPlusWinUsbConnection(string.Empty, pipes);
        var buffer = new byte[BufferLength];

        var transfer = connection.WriteOutputAsync(buffer, CancellationToken.None).AsTask();
        Assert.IsFalse(transfer.IsCompleted, "The write must return before the native operation completes.");

        pipes.Complete(ErrorSuccess);
        Assert.AreEqual(TasollerPlusTransferStatus.Succeeded, await transfer.WaitAsync(CompletionTimeout));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelKeepsTheBufferPinnedUntilWindowsSignals(bool cancelDuringSubmission)
    {
        using var cancellation = new CancellationTokenSource();
        using var buffer = new PinTrackingBuffer();
        var pipes = new TestPipeOperations(InputPipeId, ErrorIoPending, NoBytesTransferred, cancelDuringSubmission ? cancellation.Cancel : null);
        using var connection = new TasollerPlusWinUsbConnection(string.Empty, pipes);

        var transfer = connection.ReadInputAsync(buffer.Memory, cancellation.Token).AsTask();
        if (!cancelDuringSubmission)
        {
            cancellation.Cancel();
        }

        try
        {
            Assert.AreEqual(SingleCancellationRequest, pipes.CancellationRequests);
            Assert.IsFalse(transfer.IsCompleted, "Requesting cancellation must leave the transfer pending.");
            Assert.AreEqual(PendingPinCount, buffer.PinCount);
        }
        finally
        {
            if (!transfer.IsCompleted)
            {
                pipes.Complete(ErrorOperationAborted);
            }
        }

        await Assert.ThrowsAsync<OperationCanceledException>(() => transfer.WaitAsync(CompletionTimeout));
        Assert.AreEqual(ReleasedPinCount, buffer.PinCount);
    }

    [TestMethod]
    [DataRow(NoBytesTransferred, true, NoBytesTransferred)]
    [DataRow(ShortTransferLength, true, ShortTransferLength)]
    [DataRow(OverreportedReadLength, false, NoBytesTransferred)]
    public async Task AnImmediateReadAcceptsByteCountsWithinItsBuffer(int reportedByteCount, bool succeeds, int expectedByteCount)
    {
        var pipes = new TestPipeOperations(InputPipeId, ErrorSuccess, (uint)reportedByteCount);
        using var connection = new TasollerPlusWinUsbConnection(string.Empty, pipes);
        var buffer = new byte[BufferLength];

        var result = await connection.ReadInputAsync(buffer, CancellationToken.None).AsTask().WaitAsync(CompletionTimeout);

        var expectedStatus = succeeds ? TasollerPlusTransferStatus.Succeeded : TasollerPlusTransferStatus.Failed;
        Assert.AreEqual(expectedStatus, result.Status);
        Assert.AreEqual(expectedByteCount, result.BytesRead);
    }

    [TestMethod]
    public async Task AShortImmediateWriteIsReportedAsFailed()
    {
        var pipes = new TestPipeOperations(LedOutputPipeId, ErrorSuccess, ShortTransferLength);
        using var connection = new TasollerPlusWinUsbConnection(string.Empty, pipes);
        var buffer = new byte[BufferLength];

        var status = await connection.WriteOutputAsync(buffer, CancellationToken.None).AsTask().WaitAsync(CompletionTimeout);

        Assert.AreEqual(TasollerPlusTransferStatus.Failed, status);
    }

    [TestMethod]
    [DataRow(ErrorDevNotExist)]
    [DataRow(ErrorNoSuchDevice)]
    [DataRow(ErrorDeviceNotConnected)]
    [DataRow(ErrorDeviceRemoved)]
    public async Task ReadReportsDeviceRemovalWithoutLookingUpTheDevice(int error)
    {
        var pipes = new TestPipeOperations(InputPipeId, error, NoBytesTransferred);
        using var connection = new TasollerPlusWinUsbConnection(OpenedDeviceInstanceId, pipes, _ => throw new AssertFailedException("An explicit removal code does not require a device lookup."));
        var buffer = new byte[BufferLength];

        var result = await connection.ReadInputAsync(buffer, CancellationToken.None).AsTask().WaitAsync(CompletionTimeout);

        Assert.AreEqual(TasollerPlusTransferStatus.DeviceRemoved, result.Status);
        Assert.AreEqual(NoBytesTransferred, result.BytesRead);
    }

    [TestMethod]
    [DataRow(ErrorGeneralFailure, true, (int)TasollerPlusTransferStatus.DeviceRemoved)]
    [DataRow(ErrorGeneralFailure, false, (int)TasollerPlusTransferStatus.Failed)]
    [DataRow(ErrorSemaphoreTimeout, false, (int)TasollerPlusTransferStatus.Failed)]
    public async Task ReadUsesTheOpenedDevicePresenceAfterAnUnspecifiedError(int error, bool deviceAbsent, int expectedStatus)
    {
        var pipes = new TestPipeOperations(InputPipeId, error, NoBytesTransferred);
        using var connection = CreateConnectionWithDevicePresence(pipes, deviceAbsent);
        var buffer = new byte[BufferLength];

        var result = await connection.ReadInputAsync(buffer, CancellationToken.None).AsTask().WaitAsync(CompletionTimeout);

        Assert.AreEqual((TasollerPlusTransferStatus)expectedStatus, result.Status);
        Assert.AreEqual(NoBytesTransferred, result.BytesRead);
    }

    [TestMethod]
    [DataRow(true, (int)TasollerPlusTransferStatus.DeviceRemoved)]
    [DataRow(false, (int)TasollerPlusTransferStatus.Failed)]
    public async Task WriteUsesTheOpenedDevicePresenceWhenThePendingTransferFails(bool deviceAbsent, int expectedStatus)
    {
        var pipes = new TestPipeOperations(LedOutputPipeId, ErrorIoPending, NoBytesTransferred);
        using var connection = CreateConnectionWithDevicePresence(pipes, deviceAbsent);
        var buffer = new byte[BufferLength];

        var transfer = connection.WriteOutputAsync(buffer, CancellationToken.None).AsTask();
        pipes.Complete(ErrorGeneralFailure);

        Assert.AreEqual((TasollerPlusTransferStatus)expectedStatus, await transfer.WaitAsync(CompletionTimeout));
    }

    private static TasollerPlusWinUsbConnection CreateConnectionWithDevicePresence(TestPipeOperations pipes, bool deviceAbsent)
    {
        return new TasollerPlusWinUsbConnection(OpenedDeviceInstanceId, pipes, instanceId =>
        {
            Assert.AreEqual(OpenedDeviceInstanceId, instanceId);
            return deviceAbsent;
        });
    }

    private sealed class TestPipeOperations : ITasollerPlusWinUsbPipeOperations
    {
        private readonly byte _expectedPipeId;
        private readonly int _startError;
        private readonly uint _byteCount;
        private readonly Action? _beforeSubmission;
        private IntPtr _overlapped;
        private IntPtr _completionEvent;
        private int _completionError;
        private int _cancellationRequests;
        private bool _submitted;
        private bool _completed;

        public TestPipeOperations(byte expectedPipeId, int startError, uint byteCount, Action? beforeSubmission = null)
        {
            _expectedPipeId = expectedPipeId;
            _startError = startError;
            _byteCount = byteCount;
            _beforeSubmission = beforeSubmission;
        }

        public int CancellationRequests => Volatile.Read(ref _cancellationRequests);

        public int StartTransfer(byte pipeId, IntPtr buffer, uint bufferLength, IntPtr overlapped)
        {
            Assert.AreEqual(_expectedPipeId, pipeId);
            Assert.AreNotEqual(IntPtr.Zero, buffer);
            Assert.AreEqual((uint)BufferLength, bufferLength);
            _overlapped = overlapped;
            _completionEvent = Marshal.PtrToStructure<NativeOverlapped>(overlapped).EventHandle;
            _beforeSubmission?.Invoke();
            Volatile.Write(ref _submitted, true);
            if (_startError == ErrorSuccess)
            {
                Volatile.Write(ref _completed, true);
            }

            return _startError;
        }

        public (int Error, uint ByteCount) GetTransferResult(IntPtr overlapped)
        {
            Assert.AreEqual(_overlapped, overlapped);
            return Volatile.Read(ref _completed) ? (_completionError, _byteCount) : (ErrorIoIncomplete, default);
        }

        public void CancelTransfer(IntPtr overlapped)
        {
            // A cancellation requested before submission cannot cancel the future operation.
            if (Volatile.Read(ref _submitted))
            {
                Assert.AreEqual(_overlapped, overlapped);
                Interlocked.Increment(ref _cancellationRequests);
            }
        }

        public void Complete(int error)
        {
            _completionError = error;
            Volatile.Write(ref _completed, true);
            Assert.IsTrue(SetEvent(_completionEvent), "The OVERLAPPED completion event must stay valid until the transfer completes.");
        }

        public void Dispose()
        {
        }

        [DllImport("Kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetEvent(IntPtr completionEvent);
    }

    private sealed class PinTrackingBuffer : MemoryManager<byte>
    {
        private readonly byte[] _buffer;
        private int _pinCount;

        public PinTrackingBuffer()
        {
            _buffer = new byte[BufferLength];
        }

        public int PinCount => Volatile.Read(ref _pinCount);

        public override Span<byte> GetSpan() => _buffer;

        public override unsafe MemoryHandle Pin(int elementIndex = 0)
        {
            var pin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            Interlocked.Increment(ref _pinCount);
            return new MemoryHandle((void*)IntPtr.Add(pin.AddrOfPinnedObject(), elementIndex), pin, this);
        }

        public override void Unpin()
        {
            Interlocked.Decrement(ref _pinCount);
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
