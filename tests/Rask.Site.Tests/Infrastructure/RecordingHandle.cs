using Rask.Core.Live;

namespace Rask.Site.Tests.Infrastructure;

internal sealed class RecordingHandle : IRenderHandle
{
    public int RequestPublishRenderCount;
    public int RequestRenderCount;

    public Task RequestRender()
    {
        Interlocked.Increment(ref RequestRenderCount);
        return Task.CompletedTask;
    }

    public Task RequestPublishRender()
    {
        Interlocked.Increment(ref RequestPublishRenderCount);
        return Task.CompletedTask;
    }
}
