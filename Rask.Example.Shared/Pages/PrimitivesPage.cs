using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("primitives")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PrimitivesPage : Component
{
    protected override Component? Head => Title()["Primitives — Rask"];

    protected override Component Render() =>
        Fragment()[
            PageHeader.Render(
                "Primitives",
                "Four primitives sit beneath every Rask page: Text, Raw, Fragment, and Doctype. Everything else is built out of them."),
            H2(Class: "h4 mt-4 mb-3")["Text — auto-escaped strings"],
            CodeSample(
                """
                // Strings implicitly convert to Child (wrapped as Text):
                P()["1 < 2 && \"safe\""]
                """,
                Notes:
                "Text HTML-encodes its value. The < and & above render as literal characters, not parsed as markup.",
                Result: P(Class: "mb-0")["1 < 2 && \"safe\""]),
            H2(Class: "h4 mt-5 mb-3")["Raw — verbatim HTML"],
            CodeSample(
                """
                // new Raw(...) bypasses encoding. Use deliberately.
                P()[Raw("Already <strong>safe</strong> HTML")]
                """,
                Notes:
                "Raw is the escape hatch. Use when you control the source (markdown output, sanitized snippets); never on user input.",
                Result: P(Class: "mb-0")[Raw("Already <strong>safe</strong> HTML")]),
            Div(Class: "alert alert-warning d-flex align-items-start mt-3")[
                I(Class: "bi bi-shield-exclamation me-3 fs-4"),
                Div()[
                    Strong()["Security:"],
                    " ", Code()["Raw"],
                    " skips all HTML encoding. Never feed it untrusted strings — sanitize or use ",
                    Code()["Text"], " instead."
                ]
            ],
            H2(Class: "h4 mt-5 mb-3")["Fragment — siblings without a wrapper"],
            CodeSample(
                """
                Fragment(
                    H3()["A heading"],
                    P()["A paragraph"]
                )
                """,
                Notes:
                "Fragment renders its children with no surrounding tag — handy for siblings at the root, especially Fragment(Doctype(), Html(...)) as the page entry.",
                Result: Fragment()[
                    H3(Class: "h5")["A heading"],
                    P(Class: "mb-0")["A paragraph"]
                ]),
            H2(Class: "h4 mt-5 mb-3")["Doctype"],
            CodeSample(
                """
                // The recommended page-root pattern:
                Fragment(
                    Doctype(),
                    Html("en")[ /* head + body */ ]
                )
                """,
                Notes:
                "Doctype() emits exactly <!DOCTYPE html>. Special-cased — no attributes, no children, no wrapping tag.",
                Result: Span(Class: "text-secondary")["(emits ", Code()["<!DOCTYPE html>"], ")"]),
            H2(Class: "h4 mt-5 mb-3")["Children from strings"],
            CodeSample(
                """
                // Child has implicit conversions from string and Component:
                Div()[
                    "plain text, ",
                    Strong()["bold text, "],
                    $"interpolated: {DateTime.Today:yyyy-MM-dd}"
                ]
                """,
                Result: Div(Class: "mb-0")[
                    "plain text, ",
                    Strong()["bold text, "],
                    $"interpolated: {DateTime.Today:yyyy-MM-dd}"
                ])
        ];
}
