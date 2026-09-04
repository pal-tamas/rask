using Rask.Core;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IIntersectionObserver" /> — observe when an element enters/leaves the viewport. Scroll
///     the box below into view: the browser pushes the change to C#, which updates the badge (the handler
///     calls <c>StateHasChanged()</c>, the sanctioned pattern for an externally-pushed update).
/// </summary>
public sealed partial class IntersectionObserverDemo(IIntersectionObserver observer) : Component, IAsyncDisposable
{
    private readonly ElementRef _target = ElementRef.New();
    private IAsyncDisposable? _observation;
    private bool _visible;
    private int _changes;

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender || _observation is not null)
        {
            return;
        }

        _observation = await observer.ObserveAsync(_target, entry =>
        {
            _visible = entry.IsIntersecting;
            _changes++;
            StateHasChanged();
            return Task.CompletedTask;
        });
    }

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex gap-2 items-center flex-wrap mb-2")[
                    Span.Class(_visible ? $"{Tw.BadgeSuccess}" : $"{Tw.BadgeSecondary}").Id("io-status")[
                        _visible ? "in view" : "out of view"],
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
            ]
        ];

    public async ValueTask DisposeAsync()
    {
        if (_observation is not null)
        {
            await _observation.DisposeAsync();
        }
    }
}
