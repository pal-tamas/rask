using Rask.Core.Live;

namespace Rask.Example.Shared.Tests.Infrastructure;

internal sealed class RecordingHandle : IRenderHandle
{
    public int RequestRenderCount;
    public int RequestPublishRenderCount;

    public Task RequestRenderAsync()
    {
        Interlocked.Increment(ref RequestRenderCount);
        return Task.CompletedTask;
    }

    public Task RequestPublishRenderAsync()
    {
        Interlocked.Increment(ref RequestPublishRenderCount);
        return Task.CompletedTask;
    }
}
