using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("props")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PropsPage : Component
{
    protected override RenderResult Head => Title()["Universal props — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Universal props",
            "Every tag accepts Id, Class, Style, and Data. They render in that exact order, ahead of any tag-specific attributes."),
        H2(Class: "h4 mt-4 mb-3")["Id, Class, Style"],
        CodeSample(
            """
            Div(
                Id: "card-1",
                Class: "card highlighted",
                Style: "padding: 0.5rem; border: 1px solid #ccc;")["Three attributes — id then class then style."]
            """,
            Result: Div(
                Id: "card-1",
                Class: "card border-primary",
                Style: "padding: 0.6rem 0.8rem;")["Three attributes — id then class then style."]),
        H2(Class: "h4 mt-5 mb-3")["Data — dictionary expands as data-*"],
        CodeSample(
            """
            Div(
                Data: new Dictionary<string, string?> {
                    ["role"] = "card",
                    ["index"] = "7",
                    ["new"] = null   // bare attribute
                })["Inspect — data-role, data-index, and a bare data-new."]
            """,
            Notes:
            "Null values render as bare attributes (e.g. data-new). That's also how boolean attrs like disabled work elsewhere.",
            Result: Div(
                Class: "p-2 bg-light rounded border",
                Data: new Dictionary<string, string?> { ["role"] = "card", ["index"] = "7", ["new"] = null })[
                "Inspect the rendered HTML — data-role, data-index, and a bare data-new."]),
        H2(Class: "h4 mt-5 mb-3")["Attribute order"],
        CodeSample(
            """
            // Tag-specific (Href) renders AFTER id/class/style/data-*, even though
            // the factory signature lists Href first.
            A(Href: "/tags", Id: "out", Class: "link",
              Data: new Dictionary<string, string?> { ["external"] = "true" })["See HTML order"]
            """,
            Notes:
            "Render order is base props first (id, class, style, data-*), then tag-specific. Tests enforce it. Predictable for diffing and DOM tooling.",
            Result: A(
                "/tags",
                Id: "out",
                Class: "link link-primary",
                Data: new Dictionary<string, string?> { ["external"] = "true" })["See HTML order"])
    ];
}
