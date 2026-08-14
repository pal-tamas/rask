using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// App whose click handler awaits a long delay that observes CancellationToken — a *cooperative* slow
// handler. With RaskServerOptions.HandlerTimeout set, the dispatch cancels the token and the delay
// throws, so the handler unwinds instead of pinning the session. Used to exercise the handler timeout.
public sealed partial class CooperativeTimeoutApp : Component
{
    // Completed when the handler observes its CancellationToken being cancelled. Static so the test can
    // await it without a handle to the DI-constructed instance; reset per test before the host starts.
    public static TaskCompletionSource<bool> Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Component? HeadAssets => new Title()["timeout"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Button
            .OnClickAsync(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult(true);
                throw; // let the dispatch see the cancellation (records the timeout)
            }
        })["go"]
    ];
}
