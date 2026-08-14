using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Messaging;

#pragma warning disable RASK014 // test harness instantiates StubComponent directly

namespace Rask.Core.Tests.Messaging;

// ToastOutlet is the headless display half: it drains the scoped IToaster into its own list (on mount and
// on Changed) and hands the messages to a caller-owned Template with a dismiss callback. These pin the
// drain-on-mount path, the drain-on-Changed path, the consumed-once contract, dismissal, and the opt-in
// AutoDismissAfter timer.
public partial class ToastOutletTests : global::Rask.Core.RaskMarkup
{
    // The Template renders each message's text and captures the dismiss callback so a test can drive it.
    // `new`: hides the ToastOutlet entry named Outlet that the markup host brings in (CS0108).
    private static new Func<Component> Outlet(out Func<Action<int>?> dismiss)
    {
        Action<int>? captured = null;
        dismiss = () => captured;
        return () => ToastOutlet
            .Template((msgs, d) =>
        {
            captured = d;
            return Div[msgs.Select(m => (Component)Span.Key(m.Id.ToString())[m.Message])];
        });
    }

    private static (StubComponent Host, IServiceProvider Sp) Build(IToaster toast)
    {
        var sp = new ServiceCollection().AddSingleton(toast).BuildServiceProvider();
        return (new StubComponent(Outlet(out _)), sp);
    }

    [Fact]
    public void MessageQueuedBeforeMount_ShowsOnFirstRender()
    {
        IToaster toast = new Toaster();
        toast.Success("Saved"); // queued before the outlet exists — the redirect-then-show case
        var (host, sp) = Build(toast);

        var html = host.RenderAsLiveRoot(sp);

        Assert.Contains("Saved", html);
    }

    [Fact]
    public void MessageAddedAfterMount_ShowsOnReRender()
    {
        IToaster toast = new Toaster();
        var host = new StubComponent(Outlet(out _));
        var sp = new ServiceCollection().AddSingleton<IToaster>(toast).BuildServiceProvider();

        var first = host.RenderAsLiveRoot(sp); // mounts + subscribes; nothing queued yet
        Assert.DoesNotContain("Later", first);

        toast.Info("Later");                   // fires Changed → outlet drains
        var second = host.RenderAsLiveRoot(sp);

        Assert.Contains("Later", second);
    }

    [Fact]
    public void Outlet_ConsumesOnce_ServiceEmptyAfterDrain()
    {
        IToaster toast = new Toaster();
        toast.Info("once");
        var (host, sp) = Build(toast);

        host.RenderAsLiveRoot(sp); // outlet drains the queue into itself

        // The service no longer holds the message — a second outlet (or Consume) sees nothing.
        Assert.Empty(toast.Consume());
    }

    [Fact]
    public void NoMessages_RendersNothing()
    {
        IToaster toast = new Toaster();
        var (host, sp) = Build(toast);

        var html = host.RenderAsLiveRoot(sp);

        Assert.DoesNotContain("<span", html);
    }

    [Fact]
    public void Dismiss_RemovesTheMessage()
    {
        IToaster toast = new Toaster();
        toast.Warning("bye");
        var host = new StubComponent(Outlet(out var dismiss));
        var sp = new ServiceCollection().AddSingleton<IToaster>(toast).BuildServiceProvider();

        Assert.Contains("bye", host.RenderAsLiveRoot(sp));

        dismiss()!.Invoke(0); // dismiss the message with Id 0 (first queued)
        var after = host.RenderAsLiveRoot(sp);

        Assert.DoesNotContain("bye", after);
    }

    [Fact]
    public async Task AutoDismissAfter_RemovesTheMessageOnceTheDelayElapses()
    {
        IToaster toast = new Toaster();
        toast.Info("gone soon");
        var host = new StubComponent(() => ToastOutlet
            .Template((msgs, _) =>
                Div[msgs.Select(m => (Component)Span.Key(m.Id.ToString())[m.Message])])
            .AutoDismissAfter(TimeSpan.FromMilliseconds(80)));
        var sp = new ServiceCollection().AddSingleton<IToaster>(toast).BuildServiceProvider();

        // Shows on first render and schedules the one-shot 80 ms timer.
        Assert.Contains("gone soon", host.RenderAsLiveRoot(sp));

        // The timer fires on a thread-pool thread and removes the message; poll a re-render until it clears.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        string html;
        do
        {
            await Task.Delay(25);
            html = host.RenderAsLiveRoot(sp);
        } while (html.Contains("gone soon") && DateTime.UtcNow < deadline);

        Assert.DoesNotContain("gone soon", html);
    }
}
