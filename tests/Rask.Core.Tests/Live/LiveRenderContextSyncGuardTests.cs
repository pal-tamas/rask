using System.Reflection;
using Rask.Core.Live;

#pragma warning disable RASK014 // test constructs a StubComponent directly

namespace Rask.Core.Tests.Live;

public class LiveRenderContextSyncGuardTests
{
    [Fact]
    public void CurrentSync_WhenThreadHoldsDisposedContext_ReadsAsNull()
    {
        var services = RenderHarness.EmptyServices();

        LiveRenderContext disposed;
        using (var scope = RenderHarness.Render(new StubComponent(Span()), services))
        {
            disposed = scope.Context;
            Assert.Same(disposed, LiveRenderContext.CurrentSync); // active mid-render
        }

        // Re-pollute this thread the way an async render that suspended at an await leaves it:
        // _syncCurrent still points at a context that has already been disposed elsewhere.
        SetSyncMirror(disposed);
        try
        {
            Assert.False(disposed.IsActive);

            // The guard makes the stale, disposed context read as "no active context".
            Assert.Null(LiveRenderContext.CurrentSync);

            // Observable effect: a handler emits no attribute outside an active live context,
            // instead of attributing to a leftover context from an unrelated render.
            Assert.Equal("<button></button>", Button(OnClick: () => { }).ToHtml());
        }
        finally
        {
            LiveRenderContext.ResetSyncForTests();
        }
    }

    private static void SetSyncMirror(LiveRenderContext? ctx) =>
        typeof(LiveRenderContext)
            .GetField("_syncCurrent", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, ctx);
}
