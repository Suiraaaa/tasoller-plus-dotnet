using System;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus
{
    internal interface ITasollerPlusConnection : IDisposable
    {
        ValueTask<TasollerPlusReadTransferResult> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    }
}
