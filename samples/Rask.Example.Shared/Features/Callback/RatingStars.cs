namespace Rask.Example.Shared.Features;

// A reusable child component that knows nothing about its parent's state. It renders clickable
// stars and emits the chosen rating up through a plain Action<int> prop. The child wraps the
// callback in its own click handler (so the DOM event only dirties the child) and invokes it off
// that path — yet the parent still re-renders, because the framework auto-wraps the delegate to
// re-render its owner. No Action type, no StateHasChanged threaded through by hand.
public sealed partial class RatingStars : Component
{
    public int Value { get; set; }
    public Action<int>? OnRate { get; set; }

    protected override Component? Render() =>
        Div.Class("d-inline-flex gap-1")[
            Enumerable.Range(1, 5).Select(i => (Component)BsButton
                .Class("btn-link p-0 fs-3 lh-1 text-decoration-none")
                .Style(i <= Value ? "color:#ffc107" : "color:#ced4da")
                .OnClick(() => OnRate?.Invoke(i))
                .Key(i)[
                i <= Value ? "★" : "☆"
            ])
        ];
}
