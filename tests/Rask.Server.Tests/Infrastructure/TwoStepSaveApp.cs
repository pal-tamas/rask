using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// A "save" that waits on a gate between two states, and a "bump" beside it. The page also shows a number the
// test changes from outside the handler: that is what gives the repaint ahead of the handler's continuation
// something new to send — a repaint that says what the last one said is not sent at all.
public sealed partial class TwoStepSaveApp : Component
{
    public static TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Set when the code after the await has run to its end.
    public static TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static volatile int Outside;

    private string _state = "idle";

    public int Counter;

    protected override Component? HeadAssets => Markup.Title["two-step"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Markup.P[$"state={_state} outside={Outside} count={Counter}"],
        Button.OnClick(async () =>
        {
            _state = "saving";
            await Gate.Task;
            _state = "saved";
            Finished.TrySetResult();
        })["save"],
        Button.OnClick(() => Counter++)["bump"]
    ];
}
