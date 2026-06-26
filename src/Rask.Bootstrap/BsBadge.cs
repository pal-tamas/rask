namespace Rask.Bootstrap;

// A Bootstrap badge: <span class="badge text-bg-{color}">. Bootstrap 5.3 uses the contrast-aware
// text-bg-* helper so the label stays legible on light and dark colors alike.
public sealed class BsBadge : Element
{
    protected override string TagName => "span";

    public BsColor? Color { get; set; }

    // Fully rounded "pill" shape (.rounded-pill).
    public bool? Pill { get; set; }

    protected override string? ResolveClass() => BsClass.Join(
        "badge",
        Color is { } c ? c.TextBg() : null,
        Pill is true ? "rounded-pill" : null,
        Class);
}
