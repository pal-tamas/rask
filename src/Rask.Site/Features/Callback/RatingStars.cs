namespace Rask.Site.Features;

// A reusable child component that knows nothing about its parent's state. It renders clickable
// stars and emits the chosen rating up through a Callback<int> prop — non-nullable, and still
// optional: unset, `await OnRate.Invoke(i)` does nothing. The child wraps the call in its own click
// handler (so the DOM event only dirties the child) — yet the parent still re-renders, because the
// framework auto-wraps the handler to re-render its owner. No StateHasChanged threaded through by hand.
public sealed partial class RatingStars : Component
{
    public int Value { get; set; }
    public Callback<int> OnRate { get; set; }

    protected override Component? Render() =>
        Div.Class("inline-flex gap-1")[
            // Key first: it says which star this is before anything is said about it.
            //
            // The filled/empty colours are TOKENS now, not #ffc107 and #ced4da. A hardcoded hex ignores the
            // theme, so the filled star stayed amber on a palette with no amber in it and the empty one was
            // invisible on anything dark.
            Enumerable.Range(1, 5).Select(i => (Component)Ui.Button.Key(i)
                .Variant(Ui.Variant.Link)
                .Class("text-2xl leading-none " + (i <= Value ? "text-ui-warn-ink" : "text-ui-muted"))
                .OnClick(async () => await OnRate.Invoke(i))[i <= Value ? "★" : "☆"])
        ];
}
