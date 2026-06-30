namespace Rask.Bootstrap;

// A Bootstrap spinner. Wraps the core Div() with role="status" and the .spinner-border (default) or
// .spinner-grow classes. A visually-hidden status label is rendered for screen readers unless the
// caller supplies their own children.
public sealed class BsSpinner : BsBlock
{
    public BsColor? Color { get; set; }
    public BsSpinnerKind? Kind { get; set; }

    // The small variant (.spinner-border-sm / .spinner-grow-sm).
    public bool? Small { get; set; }

    // The visually-hidden status text for assistive tech; defaults to "Loading…".
    public string? Label { get; set; }

    private bool Grow => Kind == BsSpinnerKind.Grow;

    protected override RenderResult Render()
    {
        var cls = BsClass.Join(
            Grow ? "spinner-grow" : "spinner-border",
            Small is true ? (Grow ? "spinner-grow-sm" : "spinner-border-sm") : null,
            Color is { } c ? c.Text() : null,
            Class);

        var content = Children ?? [Span(Class: "visually-hidden")[Label ?? "Loading…"]];
        return Div(Id: Id, Class: cls, Role: "status")[content];
    }
}
