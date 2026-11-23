using System.Threading.Channels;

using TasollerPlus.Connections;

namespace TasollerPlus.Tests.TestSupport;

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

    public TasollerPlusTransportOpenResult Open()
    {
        return TasollerPlusTransportOpenResult.FromConnection(this);
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
