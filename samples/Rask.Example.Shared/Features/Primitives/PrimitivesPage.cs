using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("primitives")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PrimitivesPage : Component
{
    protected override Component? Head => Title()["Primitives — Rask"];

    protected override Component? Render() =>
    [
        PageHeader.Render(
            "Primitives",
            "Four primitives sit beneath every Rask page: Text, Raw, Fragment, and Doctype. Everything else is built out of them."),
        H2(Class: "h4 mt-4 mb-3")["Text — auto-escaped strings"],
        CodeSample(
            ["PrimitivesTextDemo.cs"],
            Notes:
            "Text HTML-encodes its value. The < and & above render as literal characters, not parsed as markup.",
            Result: PrimitivesTextDemo()),
        H2(Class: "h4 mt-5 mb-3")["Raw — verbatim HTML"],
        CodeSample(
            ["PrimitivesRawDemo.cs"],
            Notes:
            "Raw is the escape hatch. Use when you control the source (markdown output, sanitized snippets); never on user input.",
            Result: PrimitivesRawDemo()),
        BsAlert(Color: BsColor.Warning, Class: "d-flex align-items-start mt-3")[
            BsIcon(Name: BsIconName.ShieldExclamation, Class: "me-3 fs-4"),
            Div()[
                Strong()["Security:"],
                " ", Code()["Raw"],
                " skips all HTML encoding. Never feed it untrusted strings — sanitize or use ",
                Code()["Text"], " instead."
            ]
        ],
        H2(Class: "h4 mt-5 mb-3")["Fragment — siblings without a wrapper"],
        CodeSample(
            ["PrimitivesFragmentDemo.cs"],
            Notes:
            "Fragment renders its children with no surrounding tag — handy for siblings at the root, especially Fragment(Doctype(), Html(...)) as the page entry.",
            Result: PrimitivesFragmentDemo()),
        H2(Class: "h4 mt-5 mb-3")["Doctype"],
        CodeSample(
            ["PrimitivesDoctypeDemo.cs"],
            Notes:
            "Doctype() emits exactly <!DOCTYPE html>. Special-cased — no attributes, no children, no wrapping tag.",
            Result: PrimitivesDoctypeDemo()),
        H2(Class: "h4 mt-5 mb-3")["Children from strings"],
        CodeSample(
            ["PrimitivesChildrenDemo.cs"],
            Result: PrimitivesChildrenDemo())
    ];
}
