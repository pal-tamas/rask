using System.Text;
using Microsoft.Extensions.ObjectPool;

namespace Rask.Core.Live;

// Single source of StringBuilder pooling configuration for the render and payload paths.
// Component.ToHtml and LivePayload.InjectRootAttr were the two surviving per-call
// `new StringBuilder()` allocations after commits 607d27b/6b55aea; both now rent through
// here and return on dispose via Get()/Return(). The maximum retained capacity caps the
// per-thread memory cost of holding onto a buffer that grew unusually large for one render
// — beyond 64 KiB the pool discards it rather than retaining indefinitely.
internal static class RaskStringBuilderPool
{
    private const int InitialCapacity = 4096;
    private const int MaximumRetainedCapacity = 64 * 1024;

    public static readonly ObjectPool<StringBuilder> Shared =
        new DefaultObjectPoolProvider()
            .CreateStringBuilderPool(InitialCapacity, MaximumRetainedCapacity);
}
