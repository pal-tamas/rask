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
    private bool _sawFirstRender;
    private int _onMount;
    private int _onMountAsyncStarted;
    private bool _onMountAsyncSettled;
    private int _onPropsChanged;
    private int _onPropsChangedAsync;
    private int _onRendered;
    private int _renderCount;

    protected override void OnMount() => _onMount++;

    protected override async Task OnMountAsync()
    {
        _onMountAsyncStarted++;
        await Task.Delay(450);

        // Flips a flag the row already on screen reads. Appending a line here instead is what used to
        // grow the list by an <li> and a <code>, 450ms after the first paint.
        _onMountAsyncSettled = true;
    }

    protected override void OnPropsChanged() => _onPropsChanged++;

    protected override Task OnPropsChangedAsync()
    {
        _onPropsChangedAsync++;
        return Task.CompletedTask;
    }

    protected override void OnRendered(bool firstRender)
    {
        _onRendered++;

        // Latched, not overwritten. `firstRender` is true exactly once, and what a reader wants to know
        // is that the framework reported it -- which a "last value" would erase on the very next render.
        _sawFirstRender |= firstRender;
    }

    protected override Component? Render() =>
        [
            Div.Class("flex gap-3 items-center flex-wrap mb-3")[
                UiBadge.Tone(UiTone.Primary).Variant(UiVariant.Soft).Class("text-base")[$"Render #{++_renderCount}"],
                // The handler just records the click; Rask re-renders the component that owns the
                // callback (this probe — the lambda closes over its state) right after it runs, so the
                // badge repaints with no StateHasChanged (RASK026). Works the same through UiButton,
                // which forwards the callback down to the native <button>.
                UiButton.Tone(UiTone.Primary)
                    .OnClick(() => _clicks++)[UiIcon.Name(UiIconName.Retry), "Trigger re-render"]
            ],
            H3.Class("text-base font-semibold text-ui-muted uppercase text-sm")["Hook log"],
            UiList.Ordered(true)[
                Row("OnMount", Ran(_onMount)),
                Row("OnMountAsync (start)", Ran(_onMountAsyncStarted)),
                Row("OnMountAsync (after 450ms await)", _onMountAsyncSettled ? "resolved" : "awaiting…"),
                Row("OnPropsChanged", Ran(_onPropsChanged)),
                Row("OnPropsChangedAsync", Ran(_onPropsChangedAsync)),
                Row("OnRendered", _onRendered == 0
                    ? "not yet"
                    : $"{Ran(_onRendered)}, firstRender: {(_sawFirstRender ? "true" : "false")} on the first"),
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
