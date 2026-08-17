namespace Rask.Bootstrap;

// A Bootstrap placeholder (loading skeleton): <span class="placeholder col-{n}">. When Animation is
// set, the placeholder is wrapped in a .placeholder-glow / .placeholder-wave span so it shimmers.

/// <summary>
///     A skeleton block standing in for content that is still loading, so the layout does not jump when the
///     real content arrives.
/// </summary>
public sealed partial class BsPlaceholder : BsBlock
{
    /// <summary>The block's colour.</summary>
    public BsColor? Color { get; set; }

    /// <summary>The block's height.</summary>
    public BsSize? Size { get; set; }

    // Grid column width 1–12 (col-{n}); the usual way to size a placeholder line.

    /// <summary>The block's width in grid columns.</summary>
    public int? Col { get; set; }

    /// <summary>The shimmer to animate with, if any.</summary>
    public BsPlaceholderAnimation? Animation { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "placeholder",
            Color is { } c ? c.Bg() : null,
            Size is { } s && s.Suffix() is { } suffix ? $"placeholder-{suffix}" : null,
            Col is { } col ? $"col-{col}" : null,
            Class);

        var placeholder = Span.Id(Id).Class(cls)[Items];
        var animation = Animation ?? BsPlaceholderAnimation.None;

        return animation == BsPlaceholderAnimation.None
            ? placeholder
            : Span.Class(animation == BsPlaceholderAnimation.Wave ? "placeholder-wave" : "placeholder-glow")[placeholder];
    }
}
