namespace Rask.Bootstrap;

// An anchor styled as a Bootstrap button — the link counterpart to BsButton. BsButton wraps <button>
// (an in-page action); BsLink wraps the core A() so a real navigation/external link (href, target,
// rel) carries the same typed Color/Outline/Size/Active styling. Use this instead of the raw
// A(Class:"btn btn-primary") shape BsButton's docs used to point at. It renders a plain <a> (not an
// SPA NavLink) — for an in-app nav link that also styles as a button, wrap a NavLink yourself; this is
// the CTA/external-link case (Playground, GitHub, docs cross-links).
public sealed class BsLink : BsBlock
{
    public string? Href { get; set; }
    public string? Target { get; set; }
    public string? Rel { get; set; }

    public BsColor? Color { get; set; }

    // Outline variant (btn-outline-{color}); ignored when Color is null.
    public bool? Outline { get; set; }

    public BsSize? Size { get; set; }

    // Toggle/pressed state: adds .active and aria-pressed="true" (parity with BsButton).
    public bool? Active { get; set; }

    public new string? Style { get; set; }
    public IReadOnlyDictionary<string, string?>? Aria { get; set; }

    protected override Component? Render()
    {
        var cls = BsClass.Join(
            "btn",
            Color is { } c ? c.Button(Outline is true) : null,
            Size is { } s ? s.ButtonSize() : null,
            Active is true ? "active" : null,
            Class);

        var aria = Active is true ? BsClass.WithAria(Aria, "pressed", "true") : Aria;

        return A(Id: Id, Class: cls, Style: Style, Href: Href, Target: Target, Rel: Rel, Aria: aria)[Items];
    }
}
