using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Messaging;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Subscribes to a topic on mount and prints every headline it has received, so a frame carrying "seen=…" proves a
// broadcast reached this session and rendered there (#1061). The "bump" button lets a test interleave an event.
public sealed partial class BroadcastApp : Component
{
    public static readonly Topic<string> Headlines = new("headlines");

    private readonly IBroadcast _broadcast;
    private readonly List<string> _seen = [];
    private int _clicks;

    public BroadcastApp(IBroadcast broadcast)
    {
        _broadcast = broadcast;
        Last = this;
    }

    /// <summary>The most recently constructed instance, for a test that has to reach the page it rendered.</summary>
    public static BroadcastApp? Last { get; private set; }

    public int RenderCount { get; private set; }

    protected override Component? HeadAssets => new Title()["broadcast"];
    protected override string? HtmlLang => null;

    protected override Task Mount()
    {
        _broadcast.Subscribe(this, Headlines, headline => _seen.Add(headline));
        return Task.CompletedTask;
    }

    protected override Component? Render()
    {
        RenderCount++;
        return
        [
            new P()[$"seen={string.Join(",", _seen)};clicks={_clicks}"],
            Button.OnClick(() => _clicks++)["bump"]
        ];
    }
}
