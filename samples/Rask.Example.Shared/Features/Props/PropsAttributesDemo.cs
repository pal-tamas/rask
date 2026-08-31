namespace Rask.Example.Shared.Features;

public sealed partial class PropsAttributesDemo : Component
{
    // Two layers, and the order matters. `Lang` is a typed global property, and it is the case that
    // matters most — WCAG 3.1.2 asks for the element that CHANGES language to be marked, so a screen
    // reader switches pronunciation for the quoted phrase and not the whole page.
    //
    // `Attributes` sits underneath as the escape hatch, for what Element names no property for. Prefer
    // the typed property whenever one exists: it is checked, discoverable and documented, and nothing in
    // the bag is validated or de-duplicated. A null value renders the attribute bare.
    protected override Component? Render() =>
        P.Class("mb-0")[
            "The dish arrived with an air of ",
            Span.Class("italic").Lang("fr")["déjà vu"],
            " — and for what has no typed property, a bare ",
            Code.Attributes(new Dictionary<string, string?> { ["data-demo"] = null })["data-demo"],
            ", written verbatim."];
}
