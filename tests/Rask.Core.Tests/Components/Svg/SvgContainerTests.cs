namespace Rask.Core.Tests.Components;

public partial class SvgContainerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void An_unset_G_renders_only_its_open_and_close_tags() => Assert.Equal("<g></g>", G.ToHtml());

    [Fact]
    public void A_G_transform_set_through_the_base_is_emitted() =>
        Assert.Equal("<g transform=\"translate(10,20)\"></g>", G.Transform("translate(10,20)").ToHtml());

    [Fact]
    public void An_unset_Defs_renders_only_its_open_and_close_tags() => Assert.Equal("<defs></defs>", Defs.ToHtml());

    [Fact]
    public void An_unset_Switch_renders_only_its_open_and_close_tags() =>
        Assert.Equal("<switch></switch>", Switch.ToHtml());

    [Fact]
    public void A_desc_with_a_child_renders_the_description() =>
        Assert.Equal("<desc>a chart</desc>", Desc["a chart"].ToHtml());

    [Fact]
    public void Setting_every_Use_prop_emits_the_expected_attributes() =>
        Assert.Equal(
            "<use href=\"#icon\" x=\"1\" y=\"2\" width=\"3\" height=\"4\"></use>",
            Use.Href("#icon").X("1").Y("2").Width("3").Height("4").ToHtml());

    [Fact]
    public void Setting_every_Symbol_prop_emits_the_expected_attributes() =>
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
    public void Setting_every_Marker_prop_emits_the_expected_attributes() =>
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
    public void Setting_every_ForeignObject_prop_emits_the_expected_attributes() =>
        Assert.Equal(
            "<foreignObject x=\"1\" y=\"2\" width=\"3\" height=\"4\"></foreignObject>",
            ForeignObject.X("1").Y("2").Width("3").Height("4").ToHtml());

    [Fact]
    public void Setting_every_Image_prop_emits_the_expected_attributes() =>
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
