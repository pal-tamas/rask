using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Messaging;

#pragma warning disable RASK014 // test harness instantiates StubComponent directly

namespace Rask.Core.Tests.Messaging;

// FlashOutlet is the headless display half: it drains the scoped IFlash into its own list (on mount and
// on Changed) and hands the messages to a caller-owned Template with a dismiss callback. These pin the
// drain-on-mount path, the drain-on-Changed path, the consumed-once contract, and dismissal.
public class FlashOutletTests
{
    // The Template renders each message's text and captures the dismiss callback so a test can drive it.
    private static Func<Component> Outlet(out Func<Action<int>?> dismiss)
    {
        Action<int>? captured = null;
        dismiss = () => captured;
        return () => FlashOutlet(Template: (msgs, d) =>
        {
            captured = d;
            return Div()[msgs.Select(m => (Component)Span(Key: m.Id.ToString())[m.Message])];
        });
    }

    private static (StubComponent Host, IServiceProvider Sp) Build(IFlash flash)
    {
        var sp = new ServiceCollection().AddSingleton(flash).BuildServiceProvider();
        return (new StubComponent(Outlet(out _)), sp);
    }

    [Fact]
    public void MessageQueuedBeforeMount_ShowsOnFirstRender()
    {
        IFlash flash = new Flash();
        flash.Success("Saved"); // queued before the outlet exists — the redirect-then-show case
        var (host, sp) = Build(flash);

        var html = host.RenderAsLiveRoot(sp);

        Assert.Contains("Saved", html);
    }

    [Fact]
    public void MessageAddedAfterMount_ShowsOnReRender()
    {
        IFlash flash = new Flash();
        var host = new StubComponent(Outlet(out _));
        var sp = new ServiceCollection().AddSingleton<IFlash>(flash).BuildServiceProvider();

        var first = host.RenderAsLiveRoot(sp); // mounts + subscribes; nothing queued yet
        Assert.DoesNotContain("Later", first);

        flash.Info("Later");                   // fires Changed → outlet drains
        var second = host.RenderAsLiveRoot(sp);

        Assert.Contains("Later", second);
    }

    [Fact]
    public void Outlet_ConsumesOnce_ServiceEmptyAfterDrain()
    {
        IFlash flash = new Flash();
        flash.Info("once");
        var (host, sp) = Build(flash);

        host.RenderAsLiveRoot(sp); // outlet drains the queue into itself

        // The service no longer holds the message — a second outlet (or Consume) sees nothing.
        Assert.Empty(flash.Consume());
    }

    [Fact]
    public void NoMessages_RendersNothing()
    {
        IFlash flash = new Flash();
        var (host, sp) = Build(flash);

        var html = host.RenderAsLiveRoot(sp);

        Assert.DoesNotContain("<span", html);
    }

    [Fact]
    public void Dismiss_RemovesTheMessage()
    {
        IFlash flash = new Flash();
        flash.Warning("bye");
        var host = new StubComponent(Outlet(out var dismiss));
        var sp = new ServiceCollection().AddSingleton<IFlash>(flash).BuildServiceProvider();

        Assert.Contains("bye", host.RenderAsLiveRoot(sp));

        dismiss()!.Invoke(0); // dismiss the message with Id 0 (first queued)
        var after = host.RenderAsLiveRoot(sp);

        Assert.DoesNotContain("bye", after);
    }
}
