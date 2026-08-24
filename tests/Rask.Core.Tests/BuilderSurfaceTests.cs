namespace Rask.Core.Tests;

// The chain, end to end, in one tree: attributes, children, a keyed list and a component that is not a
// plain tag. What it pins is the SERIALIZED result, attribute order included — that order is a
// documented invariant (id, class, style, title, the plain globals, data-*, role, tabindex, aria-*,
// Attributes, then tag-specific) and a chain must not be the thing that reorders it.
//
// Note the probe is a component: the entry properties are `protected static` members of Component, so
// they are only in scope inside a component body. That is deliberate — it is what lets them be
// inherited rather than imported, which is what removes the global usings.
internal sealed partial class BuilderProbe : Component
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

public partial class BuilderSurfaceTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_chain_serializes_the_whole_tree() =>
        Assert.Equal(
            "<div id=\"root\" class=\"card\"><h1 class=\"title\">Products</h1><table><thead><tr><th>#</th><th>Name</th></tr></thead><tbody><tr data-rask-key=\"1\"><td>1</td><td>Widget</td></tr><tr data-rask-key=\"2\"><td>2</td><td>Gadget</td></tr></tbody></table><a class=\"btn\" data-rask-nav>New Product</a></div>",
            BuilderProbe.Value.ToHtml());

    [Fact]
    public void The_chain_preserves_attribute_order() =>
        Assert.Contains(
            "<div id=\"root\" class=\"card\">",
            BuilderProbe.Value.ToHtml(),
            StringComparison.Ordinal);
}
