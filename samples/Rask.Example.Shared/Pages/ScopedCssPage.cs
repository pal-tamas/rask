using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("scoped-css")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ScopedCssPage : Component
{
    protected override RenderResult Head => Title()["Scoped CSS — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Scoped CSS",
            "Drop a sibling {Component}.css file next to {Component}.cs and Rask pairs them at compile time. The framework hashes the type's full name into a stable scope id and rewrites every selector to apply only inside that component — no class-name discipline, no BEM, no leaks."),
        H2(Class: "h4 mt-4 mb-3")["Two components, same selector, no conflict"],
        CodeSample(
            // The two real components and their sibling stylesheets, embedded verbatim. Both
            // declare the same .box / .dot selectors; the CSS tab shows the unmodified source
            // that Rask rewrites per-scope to produce the isolated red/blue results.
            EmbeddedSource.Read("ScopedRed.cs", "ScopedBlue.cs"),
            Css: EmbeddedSource.Read("ScopedRed.css", "ScopedBlue.css"),
            Notes:
            "Both classes use the same .box selector. The framework stamps every rendered body element with data-{scopeId}, then rewrites .box to .box[data-{scopeId}] — same source CSS, isolated outputs.",
            Result: Div(Class: "d-flex flex-column gap-2")[
                ScopedRed(),
                ScopedBlue()
            ]),
        H2(Class: "h4 mt-5 mb-3")["How it ships"],
        P()[
            "Put ", Code()["RaskScopedStyles()"],
            " in your ", Code()["<head>"],
            ". The host decides the form: on the server it renders ",
            Code()["<link href=\"/_rask/scoped.css?v={hash}\">"],
            " served with ETag + 304 revalidation; in the WASM host the bundle is delivered through the page shell's ",
            Code()["<style id=\"rask-scoped\">"],
            " slot instead. Same call site either way. Under ",
            Code()["dotnet watch"],
            " a metadata-update handler invalidates the affected type and re-renders every open session with a fresh ",
            Code()["?v="],
            " — hot reload without a page refresh."
        ],
        H2(Class: "h4 mt-5 mb-3")["What gets rewritten"],
        Div(Class: "list-group list-group-flush mb-3")[
            Li(Class: "list-group-item d-flex align-items-start")[
                I(Class: "bi bi-check2-circle text-success me-2 mt-1"),
                Div()[
                    Code()[".list li:hover"], " becomes ",
                    Code()[".list li[data-{scopeId}]:hover"],
                    " (suffix on the last compound selector — Blazor parity)"
                ]
            ],
            Li(Class: "list-group-item d-flex align-items-start")[
                I(Class: "bi bi-check2-circle text-success me-2 mt-1"),
                Div()[Code()["@media / @supports / @container / @layer"], " recurse"]
            ],
            Li(Class: "list-group-item d-flex align-items-start")[
                I(Class: "bi bi-dash-circle text-secondary me-2 mt-1"),
                Div()[Code()["@keyframes / @font-face / @import"], " pass through untouched"]
            ],
            Li(Class: "list-group-item d-flex align-items-start")[
                I(Class: "bi bi-dash-circle text-secondary me-2 mt-1"),
                Div()["Shell tags (html, head, body, title, meta, link, script, style, base) are not stamped"]
            ]
        ],
        H2(Class: "h4 mt-5 mb-3")["Diagnostics"],
        P()[
            "A ", Code()[".css"], " file with no matching ", Code()[".cs"],
            " component in the same directory raises ", Code()["RASK015"],
            ". Two ", Code()[".css"], " files claiming the same component (e.g. ",
            Code()["Counter.css"], " in two folders) raise ", Code()["RASK016"],
            ". Opt the whole project out with ",
            Code()["<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude>"], "."
        ]
    ];
}
