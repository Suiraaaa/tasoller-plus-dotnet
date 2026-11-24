using System.Diagnostics;

using TasollerPlus.Connections;
using TasollerPlus.Tests.TestSupport;

namespace TasollerPlus.Tests.Connections;

[TestClass]
public sealed class TasollerPlusSessionTests
{
    private const int InputBufferCapacity = 1;
    private const long InitialSequenceNumber = 0;
    private const int ExpectedWritesBeforeWaitingAgain = 2;
    private static readonly byte[] FirstLedReport = { 0x11 };
    private static readonly byte[] LatestLedReport = { 0x22 };
    private static readonly TimeSpan LedReportAvailabilityTimeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task AColorQueuedAtTheResendTimeoutIsSentOnceBeforeWaitingAgain()
    {
        var resendWaitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returnTimeout = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingForAnotherColor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitingForFirstResend = true;
        var writeCount = 0;
        var connection = new SingleDeviceAdapter(Array.Empty<byte>(), _ =>
        {
            Interlocked.Increment(ref writeCount);
            return ValueTask.FromResult(TasollerPlusTransferStatus.Succeeded);
        });
        using var timeout = new CancellationTokenSource(LedReportAvailabilityTimeout);
        using var session = new TasollerPlusSession(connection, InputBufferCapacity, Stopwatch.GetTimestamp, InitialSequenceNumber, WaitForLedSignalAsync);

        Assert.IsTrue(session.TrySetPendingLedReport(FirstLedReport));
        CollectionAssert.AreEqual(FirstLedReport, await connection.ReadWrittenLedReportAsync(timeout.Token));
        await resendWaitStarted.Task.WaitAsync(timeout.Token);

        Assert.IsTrue(session.TrySetPendingLedReport(LatestLedReport));
        returnTimeout.SetResult();

        CollectionAssert.AreEqual(LatestLedReport, await connection.ReadWrittenLedReportAsync(timeout.Token));
        await waitingForAnotherColor.Task.WaitAsync(timeout.Token);
        Assert.AreEqual(ExpectedWritesBeforeWaitingAgain, Volatile.Read(ref writeCount));

        async Task<bool> WaitForLedSignalAsync(SemaphoreSlim signal, TimeSpan waitTime, CancellationToken cancellationToken)
        {
            if (waitingForFirstResend && waitTime != Timeout.InfiniteTimeSpan)
            {
                // Hold the timed-out wait until a newer color has been queued.
                waitingForFirstResend = false;
                resendWaitStarted.SetResult();
                await returnTimeout.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            var signalWait = signal.WaitAsync(Timeout.InfiniteTimeSpan, cancellationToken);
            if (!waitingForFirstResend && !signalWait.IsCompleted)
            {
                waitingForAnotherColor.TrySetResult();
            }

            return await signalWait.ConfigureAwait(false);
        }
    }
}
