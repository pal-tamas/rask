namespace Rask.Bootstrap;

// A Bootstrap badge. Wraps the core Span() with the .badge classes; Bootstrap 5.3's contrast-aware
// text-bg-* helper keeps the label legible on light and dark colors alike.
public sealed class BsBadge : BsBlock
{
    public BsColor? Color { get; set; }

    // Fully rounded "pill" shape (.rounded-pill).
    public bool? Pill { get; set; }

    public string? Style { get; set; }

    protected override Component? Render() => Span(
        Id: Id,
        Class: BsClass.Join(
            "badge",
            Color is { } c ? c.TextBg() : null,
            Pill is true ? "rounded-pill" : null,
            Class),
        Style: Style)[Items];
}
