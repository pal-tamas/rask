namespace Rask.Site.Features;

// A reusable child component that knows nothing about its parent's state. It renders clickable
// stars and emits the chosen rating up through a plain Action<int> prop. The child wraps the
// callback in its own click handler (so the DOM event only dirties the child) and invokes it off
// that path — yet the parent still re-renders, because the framework auto-wraps the delegate to
// re-render its owner. No Action type, no StateHasChanged threaded through by hand.
public sealed partial class RatingStars : Component
{
    public int Value { get; set; }
    public Callback<int>? OnRate { get; set; }

    protected override Component? Render() =>
        Div.Class("inline-flex gap-1")[
            // Key FIRST (RASK046): it decides which instance is being built, so anything set before it is
            // written to an instance the key then discards.
            //
            // The filled/empty colours are TOKENS now, not #ffc107 and #ced4da. A hardcoded hex ignores the
            // theme, so the filled star stayed amber on a palette with no amber in it and the empty one was
            // invisible on anything dark.
            Enumerable.Range(1, 5).Select(i => (Component)UiButton
                .Key(i)
                .Label(i <= Value ? "★" : "☆")
                .Variant(UiVariant.Link)
                .Class("text-2xl leading-none " + (i <= Value ? "text-ui-warn-ink" : "text-ui-muted"))
                .OnClick(() => OnRate?.Invoke(i) ?? Task.CompletedTask))
        ];
}
