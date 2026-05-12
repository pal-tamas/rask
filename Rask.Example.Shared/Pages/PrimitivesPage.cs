using Rask.Core.Routing;

namespace Rask.Example.Shared;

[Route("primitives")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PrimitivesPage : Component
{
    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Primitives",
                "Four primitives sit beneath every Rask page: Text, Raw, Fragment, and Doctype. Everything else is built out of them."),
            H2(Class: "h4 mt-4 mb-3", Children: ["Text — auto-escaped strings"]),
            Components.CodeSample(
                """
                // Strings implicitly convert to Child (wrapped as Text):
                P(Children: ["1 < 2 && \"safe\""])
                """,
                Notes:
                "Text HTML-encodes its value. The < and & above render as literal characters, not parsed as markup.",
                Result: P(Class: "mb-0", Children: ["1 < 2 && \"safe\""])),
            H2(Class: "h4 mt-5 mb-3", Children: ["Raw — verbatim HTML"]),
            Components.CodeSample(
                """
                // new Raw(...) bypasses encoding. Use deliberately.
                P(Children: [new Raw("Already <strong>safe</strong> HTML")])
                """,
                Notes:
                "Raw is the escape hatch. Use when you control the source (markdown output, sanitized snippets); never on user input.",
                Result: P(Class: "mb-0", Children: [new Raw("Already <strong>safe</strong> HTML")])),
            Div(Class: "alert alert-warning d-flex align-items-start mt-3", Children:
            [
                I(Class: "bi bi-shield-exclamation me-3 fs-4"),
                Div(Children:
                [
                    Strong(Children: ["Security:"]),
                    " ", Code(Children: ["Raw"]),
                    " skips all HTML encoding. Never feed it untrusted strings — sanitize or use ",
                    Code(Children: ["Text"]), " instead."
                ])
            ]),
            H2(Class: "h4 mt-5 mb-3", Children: ["Fragment — siblings without a wrapper"]),
            Components.CodeSample(
                """
                Fragment(
                    H3(Children: ["A heading"]),
                    P(Children: ["A paragraph"])
                )
                """,
                Notes:
                "Fragment renders its children with no surrounding tag — handy for siblings at the root, especially Fragment(Doctype(), Html(...)) as the page entry.",
                Result: Fragment(
                    H3(Class: "h5", Children: ["A heading"]),
                    P(Class: "mb-0", Children: ["A paragraph"])
                )),
            H2(Class: "h4 mt-5 mb-3", Children: ["Doctype"]),
            Components.CodeSample(
                """
                // The recommended page-root pattern:
                Fragment(
                    Doctype(),
                    Html("en", Children: [ /* head + body */ ])
                )
                """,
                Notes:
                "Doctype() emits exactly <!DOCTYPE html>. Special-cased — no attributes, no children, no wrapping tag.",
                Result: Span(Class: "text-secondary", Children: ["(emits ", Code(Children: ["<!DOCTYPE html>"]), ")"])),
            H2(Class: "h4 mt-5 mb-3", Children: ["Children from strings"]),
            Components.CodeSample(
                """
                // Child has implicit conversions from string and Component:
                Div(Children: [
                    "plain text, ",
                    Strong(Children: ["bold text, "]),
                    $"interpolated: {DateTime.Today:yyyy-MM-dd}"
                ])
                """,
                Result: Div(Class: "mb-0", Children:
                [
                    "plain text, ",
                    Strong(Children: ["bold text, "]),
                    $"interpolated: {DateTime.Today:yyyy-MM-dd}"
                ]))
        );
}
