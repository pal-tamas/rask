namespace Rask.Core.Tests.Components;

public partial class SvgContainerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_on_a_g_render_only_the_open_and_close_tags() => Assert.Equal("<g></g>", G.ToHtml());

    [Fact]
    public void G_transform_via_base_emits_transform() =>
        Assert.Equal("<g transform=\"translate(10,20)\"></g>", G.Transform("translate(10,20)").ToHtml());

    [Fact]
    public void Unset_props_on_a_defs_render_only_the_open_and_close_tags() => Assert.Equal("<defs></defs>", Defs.ToHtml());

    [Fact]
    public void Unset_props_on_a_switch_render_only_the_open_and_close_tags() =>
        Assert.Equal("<switch></switch>", Switch.ToHtml());

    [Fact]
    public void Desc_with_child_renders_description() =>
        Assert.Equal("<desc>a chart</desc>", Desc["a chart"].ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_use_emits_the_expected_attributes() =>
        Assert.Equal(
            "<use href=\"#icon\" x=\"1\" y=\"2\" width=\"3\" height=\"4\"></use>",
            Use.Href("#icon").X("1").Y("2").Width("3").Height("4").ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_symbol_emits_the_expected_attributes() =>
        Assert.Equal(
            "<symbol viewBox=\"0 0 24 24\" preserveAspectRatio=\"xMinYMin\" x=\"1\" y=\"2\" " +
            "width=\"3\" height=\"4\" refX=\"5\" refY=\"6\"></symbol>",
            Symbol
                .ViewBox("0 0 24 24")
                .PreserveAspectRatio("xMinYMin")
                .X("1")
                .Y("2")
                .Width("3")
                .Height("4")
                .RefX("5")
                .RefY("6").ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_marker_emits_the_expected_attributes() =>
        Assert.Equal(
            "<marker markerWidth=\"10\" markerHeight=\"10\" refX=\"5\" refY=\"5\" orient=\"auto\" " +
            "markerUnits=\"strokeWidth\" viewBox=\"0 0 10 10\" preserveAspectRatio=\"none\"></marker>",
            Marker
                .MarkerWidth("10")
                .MarkerHeight("10")
                .RefX("5")
                .RefY("5")
                .Orient("auto")
                .MarkerUnits("strokeWidth")
                .ViewBox("0 0 10 10")
                .PreserveAspectRatio("none").ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_foreign_object_emits_the_expected_attributes() =>
        Assert.Equal(
            "<foreignObject x=\"1\" y=\"2\" width=\"3\" height=\"4\"></foreignObject>",
            ForeignObject.X("1").Y("2").Width("3").Height("4").ToHtml());

    [Fact]
    public void Setting_every_prop_on_a_image_emits_the_expected_attributes() =>
        Assert.Equal(
            "<image x=\"1\" y=\"2\" width=\"3\" height=\"4\" href=\"/a.png\" " +
            "preserveAspectRatio=\"xMidYMid slice\"></image>",
            Image
                .X("1")
                .Y("2")
                .Width("3")
                .Height("4")
                .Href("/a.png")
                .PreserveAspectRatio("xMidYMid slice").ToHtml());
}
