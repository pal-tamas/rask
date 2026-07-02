namespace Rask.Bootstrap;

// A Bootstrap placeholder (loading skeleton): <span class="placeholder col-{n}">. When Animation is
// set, the placeholder is wrapped in a .placeholder-glow / .placeholder-wave span so it shimmers.
public sealed class BsPlaceholder : BsBlock
{
    public BsColor? Color { get; set; }
    public BsSize? Size { get; set; }

    // Grid column width 1–12 (col-{n}); the usual way to size a placeholder line.
    public int? Col { get; set; }

    public BsPlaceholderAnimation? Animation { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "placeholder",
            Color is { } c ? c.Bg() : null,
            Size is { } s && s.Suffix() is { } suffix ? $"placeholder-{suffix}" : null,
            Col is { } col ? $"col-{col}" : null,
            Class);

        var placeholder = Span(Id: Id, Class: cls)[Items];
        var animation = Animation ?? BsPlaceholderAnimation.None;

        return animation == BsPlaceholderAnimation.None
            ? placeholder
            : Span(Class: animation == BsPlaceholderAnimation.Wave ? "placeholder-wave" : "placeholder-glow")[placeholder];
    }
}
