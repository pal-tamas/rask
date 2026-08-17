namespace Rask.Bootstrap;

// An anchor styled as a Bootstrap button — the link counterpart to BsButton. BsButton wraps <button>
// (an in-page action); BsLink wraps the core A() so a real navigation/external link (href, target,
// rel) carries the same typed Color/Outline/Size/Active styling. Use this instead of the raw
// A(Class:"btn btn-primary") shape BsButton's docs used to point at. It renders a plain <a> (not an
// SPA NavLink) — for an in-app nav link that also styles as a button, wrap a NavLink yourself; this is
// the CTA/external-link case (Playground, GitHub, docs cross-links).

/// <summary>
///     An anchor styled as a Bootstrap button or link. Use it whenever the action is navigation — screen
///     readers and middle-click both depend on it really being an <c>a</c>.
/// </summary>
public sealed partial class BsLink : BsBlock
{
    /// <summary>Where the link goes. Prefer a generated route URL over a hand-written path.</summary>
    public string? Href { get; set; }

    /// <summary>
    ///     Which browsing context opens the link. Pair <c>_blank</c> with <c>Rel: "noopener"</c>.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>The relationship to the target, space-separated.</summary>
    public string? Rel { get; set; }

    /// <summary>The semantic colour of the button styling.</summary>
    public BsColor? Color { get; set; }

    // Outline variant (btn-outline-{color}); ignored when Color is null.

    /// <summary>Draws it outlined rather than filled.</summary>
    public bool? Outline { get; set; }

    /// <summary>Makes it smaller or larger than the default.</summary>
    public BsSize? Size { get; set; }

    // Toggle/pressed state: adds .active and aria-pressed="true" (parity with BsButton).

    /// <summary>Renders the active state.</summary>
    public bool? Active { get; set; }

    /// <summary>
    ///     Inline CSS on the rendered element. Reach for a Bootstrap utility class through <c>Class</c>
    ///     first — inline style beats every stylesheet rule and cannot be overridden by a theme, so keep it
    ///     for values only known at runtime.
    /// </summary>
    public new string? Style { get; set; }
    /// <summary>
    ///     ARIA states and properties on the rendered element. Each entry emits <c>aria-{key}="{value}"</c>,
    ///     so <c>.Aria("label", "Close")</c> renders <c>aria-label="Close"</c> — give the key without the
    ///     prefix.
    ///     <para>
    ///         State belongs here as much as labels: <c>aria-expanded</c> and <c>aria-current</c> have to
    ///         change as the component does, or assistive technology is told the opposite of what is shown.
    ///     </para>
    /// </summary>
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

        return A.Id(Id).Class(cls).Style(Style).Href(Href).Target(Target).Rel(Rel).Aria(aria)[Items];
    }
}
