using Rask.Core;
using Rask.Core.Components;
using Rask.Html.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// A gated handler for the shutdown-drain tests, deliberately NOT sharing GatedCounterApp.
//
// That app's Gate is a mutable static, and HandlerExceptionIsolationTests already owns it. xUnit runs test
// classes in parallel, so a second class reassigning the same static races the first: one test replaces the
// TaskCompletionSource the other is parked on, and both fail intermittently. Cheaper to have our own app
// than to serialize two unrelated classes against each other.
public sealed partial class DrainGateApp : Component
{
    public static TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Counter;

    protected override Component? HeadAssets => new Title()["drain"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        new P()[$"count={Counter}"],
        Button
            .OnClickAsync(async () =>
        {
            await Gate.Task;
            Counter++;
        })["hang"]
    ];
}
