using System;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus
{
    internal interface ITasollerPlusConnection : IDisposable
    {
        // Reads and writes can be pending at the same time. Both observe cancellation.
        ValueTask<TasollerPlusReadTransferResult> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken);

        // Returns Succeeded after writing the entire report.
        ValueTask<TasollerPlusTransferStatus> WriteOutputAsync(ReadOnlyMemory<byte> report, CancellationToken cancellationToken);
    }
}
