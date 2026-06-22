using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// App with a gated "hang" handler (holds the dispatch lock until the test releases the gate) plus
// a "bump" handler that changes visible state. Lets a test park a handler across a disconnect, then
// prove the reconnected socket resumes processing the queued bump once the chain head clears — the
// counter changing means the resume render can't be hidden by HTML dedup.
public sealed class GatedCounterApp : Component
{
    public static TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Counter;

    protected override RenderResult Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["gated"]],
            new Body()[
                new P()[$"count={Counter}"],
                Button(OnClickAsync: async () => await Gate.Task)["hang"],
                Button(OnClick: () => Counter++)["bump"]]]
    ];
}
