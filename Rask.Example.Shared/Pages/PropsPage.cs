using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Rask.Example.Shared;

[Route("props"), ParentRoute(typeof(ShowcaseLayout))]
public sealed class PropsPage : Component
{
    public override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Universal props",
                "Every tag accepts Id, Class, Style, and Data. They render in that exact order, ahead of any tag-specific attributes."),

            H2(Class: "h4 mt-4 mb-3", Children: ["Id, Class, Style"]),
            Components.CodeSample(
                Source: """
                    Div(
                        Id: "card-1",
                        Class: "card highlighted",
                        Style: "padding: 0.5rem; border: 1px solid #ccc;",
                        Children: ["Three attributes — id then class then style."])
                    """,
                Result: Div(
                    Id: "card-1",
                    Class: "card border-primary",
                    Style: "padding: 0.6rem 0.8rem;",
                    Children: ["Three attributes — id then class then style."])),

            H2(Class: "h4 mt-5 mb-3", Children: ["Data — dictionary expands as data-*"]),
            Components.CodeSample(
                Source: """
                    Div(
                        Data: new Dictionary<string, string?> {
                            ["role"] = "card",
                            ["index"] = "7",
                            ["new"] = null   // bare attribute
                        },
                        Children: ["Inspect — data-role, data-index, and a bare data-new."])
                    """,
                Notes: "Null values render as bare attributes (e.g. data-new). That's also how boolean attrs like disabled work elsewhere.",
                Result: Div(
                    Class: "p-2 bg-light rounded border",
                    Data: new Dictionary<string, string?>
                    {
                        ["role"] = "card",
                        ["index"] = "7",
                        ["new"] = null
                    },
                    Children: ["Inspect the rendered HTML — data-role, data-index, and a bare data-new."])),

            H2(Class: "h4 mt-5 mb-3", Children: ["Attribute order"]),
            Components.CodeSample(
                Source: """
                    // Tag-specific (Href) renders AFTER id/class/style/data-*, even though
                    // the factory signature lists Href first.
                    A(Href: "https://example.com", Id: "out", Class: "link",
                      Data: new Dictionary<string, string?> { ["external"] = "true" },
                      Children: ["See HTML order"])
                    """,
                Notes: "Render order is base props first (id, class, style, data-*), then tag-specific. Tests enforce it. Predictable for diffing and DOM tooling.",
                Result: A(
                    Href: "https://example.com",
                    Id: "out",
                    Class: "link link-primary",
                    Data: new Dictionary<string, string?> { ["external"] = "true" },
                    Children: ["See HTML order"]))
        );
}
