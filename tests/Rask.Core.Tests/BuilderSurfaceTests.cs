using static Rask.Core.Tests.Generated;

namespace Rask.Core.Tests;

// PROTOTYPE — proves the builder surface is faithful: the same tree written in the entry-property /
// setter syntax must serialize byte-identically to the generated-factory syntax, including attribute
// order (which the framework asserts elsewhere and must not change).
//
// Note the probes are components: the entry properties are `protected static` members of Component,
// so they are only in scope inside a component body. That is deliberate — it is what lets them be
// inherited rather than imported, which is what removes the global usings.
internal sealed class BuilderProbe : Component
{
    protected override Component? Render() =>
        Div.Id("root").Class("card")[
            H1.Class("title")["Products"],
            Table[
                Thead[
                    Tr[Th["#"], Th["Name"]]
                ],
                Tbody[
                    Tr.Key(1)[Td["1"], Td["Widget"]],
                    Tr.Key(2)[Td["2"], Td["Gadget"]]
                ]
            ],
            NavLink.Class("btn")["New Product"]
        ];
}

internal sealed class FactoryProbe : Component
{
    protected override Component? Render() =>
        Div(Id: "root", Class: "card")[
            H1(Class: "title")["Products"],
            Table()[
                Thead()[
                    Tr()[Th()["#"], Th()["Name"]]
                ],
                Tbody()[
                    Tr(Key: 1)[Td()["1"], Td()["Widget"]],
                    Tr(Key: 2)[Td()["2"], Td()["Gadget"]]
                ]
            ],
            NavLink(Class: "btn")["New Product"]
        ];
}

// Both surfaces are valid in the same expression — this is what makes migration incremental.
internal sealed class MixedProbe : Component
{
    protected override Component? Render() => Div()[Span()["a"], P["b"]];
}

public class BuilderSurfaceTests
{
    [Fact]
    public void Builder_surface_renders_identically_to_the_factory() =>
        Assert.Equal(FactoryProbe().ToHtml(), BuilderProbe().ToHtml());

    [Fact]
    public void Builder_surface_preserves_attribute_order() =>
        Assert.Contains(
            "<div id=\"root\" class=\"card\">",
            BuilderProbe().ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void Both_surfaces_compose_in_one_tree() =>
        Assert.Equal("<div><span>a</span><p>b</p></div>", MixedProbe().ToHtml());
}
