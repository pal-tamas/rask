using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// App whose click handler blocks on a test-controlled gate, stalling the handler-dispatch
// chain head so queued dispatches pile up — used to exercise the backpressure circuit-breaker
// (RaskEndpointExtensions.MaxPendingHandlers).
public sealed class HangingApp : Component
{
    // Released by the test to let the hung handler complete. Static so the test can signal it
    // without a handle to the DI-constructed instance; reset per test before the host starts.
    public static TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override RenderResult Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["hang"]],
            new Body()[
                Button(OnClickAsync: async () => await Gate.Task)["hang"]]]
    ];
}
