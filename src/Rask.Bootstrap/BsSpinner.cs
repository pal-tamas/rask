namespace Rask.Bootstrap;

// A Bootstrap spinner. Wraps the core Div() with role="status" and the .spinner-border (default) or
// .spinner-grow classes. A visually-hidden status label is rendered for screen readers unless the
// caller supplies their own children.

/// <summary>
///     A loading indicator. Give it a <c>Label</c> — a bare spinner tells a screen-reader user nothing
///     about what is happening.
/// </summary>
public sealed partial class BsSpinner : BsBlock
{
    /// <summary>The semantic colour.</summary>
    public BsColor? Color { get; set; }

    /// <summary>Border or grow.</summary>
    public BsSpinnerKind? Kind { get; set; }

    // The small variant (.spinner-border-sm / .spinner-grow-sm).

    /// <summary>Renders the compact size.</summary>
    public new bool? Small { get; set; }

    // The visually-hidden status text for assistive tech; defaults to "Loading…".

    /// <summary>The text announced to assistive technology while it spins.</summary>
    public new string? Label { get; set; }

    private bool Grow => Kind == BsSpinnerKind.Grow;

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            Grow ? "spinner-grow" : "spinner-border",
            Small is true ? (Grow ? "spinner-grow-sm" : "spinner-border-sm") : null,
            Color is { } c ? c.Text() : null,
            Class);

        var content = Children ?? [Span.Class("visually-hidden")[Label ?? "Loading…"]];
        return Div.Id(Id).Class(cls).Role("status")[content];
    }
}
