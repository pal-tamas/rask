namespace Rask.Bootstrap;

// A Bootstrap badge. Wraps the core Span() with the .badge classes; Bootstrap 5.3's contrast-aware
// text-bg-* helper keeps the label legible on light and dark colors alike.

/// <summary>
///     A small count or label. It carries no semantics of its own, so if the number means something ("3
///     unread"), put that in the surrounding text or an <c>Aria</c> label.
/// </summary>
public sealed partial class BsBadge : BsBlock
{
    /// <summary>The semantic colour.</summary>
    public BsColor? Color { get; set; }

    // Fully rounded "pill" shape (.rounded-pill).

    /// <summary>Renders fully rounded.</summary>
    public bool? Pill { get; set; }

    public new string? Style { get; set; }

    protected override Component? Render() => Span
        .Id(Id)
        .Class(BsClass.Join(
            "badge",
            Color is { } c ? c.TextBg() : null,
            Pill is true ? "rounded-pill" : null,
            Class))
        .Style(Style)[Items];
}
