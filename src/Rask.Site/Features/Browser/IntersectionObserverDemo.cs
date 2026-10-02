
namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>IntersectionObserver</c>, from Rask.Web — observe when an element enters or leaves the viewport. Scroll
///     the box below into view: the browser hands the entries to the C# handler, which re-renders this component.
/// </summary>
public sealed partial class IntersectionObserverDemo : Component
{
    private readonly ElementRef _target = ElementRef.New();
    private Types.IntersectionObserver? _observer;
    private bool _visible;
    private int _changes;

    protected override async Task OnFirstRender()
    {
        _observer ??= await IntersectionObserver.Create(entries =>
        {
            _visible = entries[^1].IsIntersecting;
            _changes++;
        });
        await _observer.Observe(_target);
    }

    protected override async Task OnUnmount()
    {
        if (_observer is not null)
        {
            await _observer.Disconnect();
            await _observer.DisposeAsync();
        }
    }

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 items-center flex-wrap mb-2")[
                    Ui.Badge
                        .Tone(_visible ? Ui.Tone.Success : Ui.Tone.Neutral)
                        .Soft
                        .Id("io-status")[_visible ? "in view" : "out of view"],
                    Span.Class("text-sm text-ui-muted").Id("io-changes")[$"{_changes} change(s)"]
                ],
                P.Class("text-sm text-ui-muted mb-2")["Scroll down — the target reports when it enters the viewport."],
                // A tall spacer so the target starts below the fold, then the observed target.
                Div.Style("height: 130vh"),
                Div
                    .Ref(_target)
                    .Id("io-target")
                    .Class("p-4 rounded text-center " + (_visible ? "bg-success-subtle" : "bg-ui-well"))[
                    "🎯 observed target"
                ]
            ];
}
