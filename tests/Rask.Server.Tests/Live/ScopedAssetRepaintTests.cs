using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Live;

/// <summary>
///     A scoped asset's registration repaints the host's live sessions only when it replaced or dropped one a page may
///     be showing. An assembly loaded lazily mid-run registers its assets from its module initializer, for the first
///     time: repainting every session for that re-rendered every reader in production for nothing.
/// </summary>
public sealed class ScopedAssetRepaintTests
{
    [Fact]
    public async Task A_first_registration_after_sessions_exist_repaints_nothing()
    {
        var (session, child, repaint) = SessionShowingAChild();

        await repaint.Changed(new ScopedAssetChangedEventArgs(typeof(Shown), AssetKind.Css, replaced: false));
        await repaint.Changed(new ScopedAssetChangedEventArgs(typeof(Shown), AssetKind.Js, replaced: false));
        session.RenderInitialRoot();

        Assert.Equal(1, child.RenderCount);
    }

    [Fact]
    public async Task A_changed_registration_repaints_every_session()
    {
        var (session, child, repaint) = SessionShowingAChild();

        await repaint.Changed(new ScopedAssetChangedEventArgs(typeof(Shown), AssetKind.Css, replaced: true));
        session.RenderInitialRoot();

        Assert.Equal(2, child.RenderCount);
    }

    // A session rendered once, so its child is cached: only a repaint that marks the tree dirty renders it again.
    private static (LiveSession Session, Shown Child, ScopedAssetRepaint Repaint) SessionShowingAChild()
    {
        var store = new LiveSessionStore(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());
        var child = new Shown();
        var session = store.Create(_ => new Host(child));
        session.RenderInitialRoot();
        return (session, child, new ScopedAssetRepaint(store));
    }

    private sealed class Shown : Component
    {
        public int RenderCount;

        protected override Component? Render()
        {
            RenderCount++;
            return Markup.Span;
        }
    }

    private sealed class Host(Component child) : Component
    {
        protected override Component? Render()
        {
            var c = LiveRenderContext.Current!.GetOrCreate(_ => child);
            LiveRenderContext.NotifyParameters(c, false);
            return c;
        }
    }
}
