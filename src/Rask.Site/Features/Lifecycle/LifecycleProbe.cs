namespace Rask.Site.Features;

// One row per hook, fixed from the first paint, with each hook's own status as TEXT.
//
// It used to append to a growing log, which made the row COUNT a function of how many times the
// component had rendered and of whether a 450ms await had resolved yet -- so the markup's shape
// depended on when you looked at it. DemoMarkupGoldenTests snapshots every demo's skeleton (tag names
// and classes), and the contract it states is that a demo may change as it settles, but the moving
// part has to live in text, an id or a data-* attribute. A tag that arrives on a timer cannot be
// snapshotted by anyone.
//
// Reading the hooks as a fixed table is also the better demo: the full order is visible before anything
// has fired, a hook that has not run yet says so rather than being absent, and the counts make it
// obvious which hooks run once per mount and which run on every render.
public sealed partial class LifecycleProbe : Component
{
    private int _clicks;
    private int _onFirstRendered;
    private int _onMount;
    private bool _onMountSettled;
    private int _onUpdated;
    private int _onRendered;
    private int _renderCount;

    protected override async Task OnMount()
    {
        // Above the first await, so it still runs before the first render — which is what the
        // synchronous twin used to be for, back when there were two of these.
        _onMount++;
        await Task.Delay(450);

        // Flips a flag the row already on screen reads. Appending a line here instead is what used to
        // grow the list by an <li> and a <code>, 450ms after the first paint.
        _onMountSettled = true;
    }

    protected override Task OnUpdated()
    {
        _onUpdated++;
        return Task.CompletedTask;
    }

    protected override Task OnFirstRendered()
    {
        // Counted, not latched: its own hook now, and the count beside OnRendered's is what shows one
        // fires once per mount while the other fires per render — which a bool could only assert.
        _onFirstRendered++;
        return Task.CompletedTask;
    }

    protected override Task OnRendered()
    {
        _onRendered++;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
        [
            Div.Class("flex gap-3 items-center flex-wrap mb-3")[
                Ui.Badge.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Soft).Class("text-base")[$"Render #{++_renderCount}"],
                // The handler just records the click; Rask re-renders the component that owns the
                // callback (this probe — the lambda closes over its state) right after it runs, so the
                // badge repaints with no StateHasChanged (RASK026). Works the same through Ui.Button,
                // which forwards the callback down to the native <button>.
                Ui.Button.Tone(Ui.Tone.Primary)
                    .OnClick(() => _clicks++)[Ui.Icon.Name(Ui.IconName.Retry), "Trigger re-render"]
            ],
            H3.Class("text-base font-semibold text-ui-muted uppercase text-sm")["Hook log"],
            Ui.List.Ordered(true)[
                Row("OnMount (before its await)", Ran(_onMount)),
                Row("OnMount (after a 450ms await)", _onMountSettled ? "resolved" : "awaiting…"),
                Row("OnUpdated", Ran(_onUpdated)),
                Row("OnFirstRendered", Ran(_onFirstRendered)),
                Row("OnRendered", _onRendered == 0 ? "not yet" : Ran(_onRendered)),
                Row("Button clicks", Ran(_clicks))
            ]
        ];

    private static string Ran(int times) => times switch
    {
        0 => "not yet",
        1 => "ran 1x",
        _ => $"ran {times}x"
    };

    // Every row is the same shape, so the skeleton is a constant of this component rather than a
    // function of its state. `data-hook` gives a test something stable to select on — a data-* attribute
    // is explicitly one of the places the golden contract allows a value to move.
    private static Component Row(string hook, string status) =>
        Li.Class("ps-2")
            .Data(new Dictionary<string, string?> { ["hook"] = hook })[
            Code.Class("text-sm")[hook],
            Span.Class("text-ui-muted ms-2")[status]
        ];
}
