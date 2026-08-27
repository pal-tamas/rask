using System.Collections.Concurrent;
using Rask.Core;
using Rask.Core.Diagnostics;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Endpoints;

// Interactivity is judged from what a render DID — a handler, a form, a ref, a JS call, unsettled
// async work. A component that pushes from a timer or an event subscription does none of those during
// the walk, so its page is judged static and its updates then reach nobody. That is the one failure
// mode static rendering cannot detect, and the reason StaticPages ships off.
//
// It cannot be detected, but it CAN be reported the moment it happens. These pin that.
//
// Serialised: RaskDiagnostics.Sink is process-global, and two tests swapping it concurrently lose it
// entirely — an empty collection rather than a slow one.
[Collection("StaticPageAudit")]
public class StaticPageAuditTests
{
    [Fact]
    public async Task APushToAPageServedWithoutASession_IsReported()
    {
        using var host = RaskTestHost.Create<PushAfterResponseApp>(
            configureServer: o => o.RenderModes.Static = true);

        // Installed AFTER the host: UseRask routes RaskDiagnostics.Sink into its own ILogger bridge,
        // so a sink set beforehand is simply replaced and captures nothing.
        var captured = new ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Enqueue;
        try
        {
            var body = await host.Http.GetStringAsync("/");
            // Nothing in the render needed a connection, so it really was served as a document.
            Assert.DoesNotContain("data-rask-root", body);
            Assert.Equal(0, host.Store.Count);

            // Now the detached loop pushes, into a session that no longer exists.
            Assert.True(
                await PushAfterResponseApp.Pushed.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                "the app never pushed");

            var warning = await WaitForWarningAsync(captured, TimeSpan.FromSeconds(5));
            Assert.NotNull(warning);
            Assert.Equal("Rask.Ssr", warning!.Value.Category);
            Assert.Contains("reached nobody", warning.Value.Message);
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    [Fact]
    public async Task AnOrdinaryPageTeardown_IsNotReported()
    {
        // A normal disposal also raises a render request from unmount callbacks. Reporting that would
        // make the audit noise, and noise is how a real warning gets ignored.
        using var host = RaskTestHost.Create<HandlerOnlyApp>(
            configureServer: o => o.RenderModes.Static = true);

        // Same ordering rule as above — the host claims the sink when it is built.
        var captured = new ConcurrentQueue<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Enqueue;
        try
        {
            var body = await host.Http.GetStringAsync("/");
            // It has a handler, so it kept its session — the opposite branch.
            Assert.Contains("data-rask-root", body);

            await Task.Delay(200);

            Assert.DoesNotContain(captured, e => e.Category == "Rask.Ssr");
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }
    }

    private static async Task<RaskDiagnosticEvent?> WaitForWarningAsync(
        ConcurrentQueue<RaskDiagnosticEvent> captured, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var e in captured)
            {
                if (e.Category == "Rask.Ssr")
                {
                    return e;
                }
            }

            await Task.Delay(50);
        }

        return null;
    }
}

[CollectionDefinition("StaticPageAudit", DisableParallelization = true)]
public sealed class StaticPageAuditCollection;

// Renders nothing a walk can see as needing a connection, then pushes anyway — the shape of a polling
// panel, and of any component driven by a timer or an event subscription.
public sealed partial class PushAfterResponseApp : Component
{
    public static readonly TaskCompletionSource<bool> Pushed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string _value = "initial";

    protected override Component? HeadAssets => Title["push-after-response"];

    protected override Task OnMountAsync()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(120);
            _value = "pushed";
            StateHasChanged();
            Pushed.TrySetResult(true);
        });

        return Task.CompletedTask;
    }

    protected override Component? Render() => Div[_value];
}

public sealed partial class HandlerOnlyApp : Component
{
    private int _count;

    protected override Component? HeadAssets => Title["handler-only"];

    protected override Component? Render() => Button.OnClick(() => _count++)[$"count {_count}"];
}
