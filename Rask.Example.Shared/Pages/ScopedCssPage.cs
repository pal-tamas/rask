using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("scoped-css")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ScopedCssPage : Component
{
    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Scoped CSS",
                "Override protected override string? Css on your component to colocate styles. Rask hashes the type's full name into a stable scope id and rewrites every selector to apply only inside that component."),
            H2(Class: "h4 mt-4 mb-3", Children: ["Two components, same selector, no conflict"]),
            Demos.Components.CodeSample(
                """""
                public sealed class ScopedRed : Component
                {
                    protected override string? Css => """
                        .box { background: #fde0e0; color: #8a1f1f; ... }
                        """;
                    public override Component Render() =>
                        Div(Class: "box", Children: ["I think .box should be red."]);
                }

                public sealed class ScopedBlue : Component
                {
                    protected override string? Css => """
                        .box { background: #dde6ff; color: #1c357a; ... }
                        """;
                    public override Component Render() =>
                        Div(Class: "box", Children: ["I think .box should be blue."]);
                }
                """"",
                Notes:
                "The framework stamps every rendered body element with data-{scopeId}, then rewrites \".box\" to \".box[data-{scopeId}]\". Same source CSS, isolated outputs.",
                Result: Div(Class: "d-flex flex-column gap-2", Children:
                [
                    Demos.Components.ScopedRed(),
                    Demos.Components.ScopedBlue()
                ])),
            H2(Class: "h4 mt-5 mb-3", Children: ["How it ships"]),
            P(Children:
            [
                "Put ", Code(Children: ["RaskScopedStyles()"]),
                " in your ", Code(Children: ["<head>"]),
                " — that emits a single ", Code(Children: ["<link href=\"/_rask/scoped.css?v={hash}\">"]),
                ". The bundle is served with ETag + 304 revalidation. Under ",
                Code(Children: ["dotnet watch"]),
                " a metadata-update handler invalidates the affected type and re-renders every open session with a fresh ",
                Code(Children: ["?v="]),
                " — hot reload without a page refresh."
            ]),
            H2(Class: "h4 mt-5 mb-3", Children: ["What gets rewritten"]),
            Div(Class: "list-group list-group-flush mb-3", Children:
            [
                Li(Class: "list-group-item d-flex align-items-start", Children:
                [
                    I(Class: "bi bi-check2-circle text-success me-2 mt-1"),
                    Div(Children:
                    [
                        Code(Children: [".list li:hover"]), " becomes ",
                        Code(Children: [".list li[data-{scopeId}]:hover"]),
                        " (suffix on the last compound selector — Blazor parity)"
                    ])
                ]),
                Li(Class: "list-group-item d-flex align-items-start", Children:
                [
                    I(Class: "bi bi-check2-circle text-success me-2 mt-1"),
                    Div(Children: [Code(Children: ["@media / @supports / @container / @layer"]), " recurse"])
                ]),
                Li(Class: "list-group-item d-flex align-items-start", Children:
                [
                    I(Class: "bi bi-dash-circle text-secondary me-2 mt-1"),
                    Div(Children: [Code(Children: ["@keyframes / @font-face / @import"]), " pass through untouched"])
                ]),
                Li(Class: "list-group-item d-flex align-items-start", Children:
                [
                    I(Class: "bi bi-dash-circle text-secondary me-2 mt-1"),
                    Div(Children:
                        ["Shell tags (html, head, body, title, meta, link, script, style, base) are not stamped"])
                ])
            ])
        );
}
