using Rask.Core.Live;

namespace Rask.Site.Tests.Infrastructure;

internal sealed class RecordingHandle : IRenderHandle
{
    public int RequestPublishRenderCount;
    public int RequestRenderCount;

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
