namespace Rask.Example.Shared.Features;

public sealed partial class PropsAttributesDemo : Component
{
    // Attributes is the escape hatch: anything Element does not name a property for, emitted verbatim.
    // `lang` is the case that matters — WCAG 3.1.2 asks for the element that CHANGES language to be
    // marked, so a screen reader switches pronunciation for the quoted phrase and not the whole page.
    protected override Component? Render() =>
        P.Class("mb-0")[
            "The dish arrived with an air of ",
            Span.Class("fst-italic").Attributes(new Dictionary<string, string?> { ["lang"] = "fr" })[
                "déjà vu"],
            " — and a bare attribute, ",
            Code.Attributes(new Dictionary<string, string?> { ["data-demo"] = null })["data-demo"],
            ", written the same way."];
}
