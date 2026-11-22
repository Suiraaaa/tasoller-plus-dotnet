using System;
using System.Threading;
using System.Threading.Tasks;

namespace TasollerPlus
{
    internal interface ITasollerPlusConnection : IDisposable
    {
        ValueTask<int> ReadInputAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    }
}
