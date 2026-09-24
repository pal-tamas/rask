using Rask.Core.ScopedAssets;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// Exercises the shared SvgElement base: presentation attributes, their rendered order, click
// handlers, child nesting, and scoped-CSS stamping. Circle stands in as a representative tag.
public partial class SvgElementTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_presentation_props_emit_nothing() =>
        Assert.Equal("<circle></circle>", Circle.ToHtml());

    [Fact]
    public void Presentation_props_emit_hyphenated_attributes() =>
        Assert.Equal(
            "<circle fill=\"red\" stroke=\"black\" stroke-width=\"2\"></circle>",
            Circle.Fill("red").Stroke("black").StrokeWidth("2").ToHtml());

    [Fact]
    public void Presentation_props_come_in_declared_order_before_the_geometry() =>
        Assert.Equal(
            "<circle id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" " +
            "fill=\"f\" fill-opacity=\"fo\" fill-rule=\"fr\" stroke=\"st\" stroke-width=\"sw\" " +
            "stroke-opacity=\"so\" stroke-linecap=\"slc\" stroke-linejoin=\"slj\" " +
            "stroke-dasharray=\"sda\" stroke-dashoffset=\"sdo\" opacity=\"o\" transform=\"t\" " +
            "clip-path=\"cp\" color=\"col\" display=\"d\" visibility=\"vis\" pointer-events=\"pe\" " +
            "cx=\"1\" cy=\"2\" r=\"3\"></circle>",
            Circle
                .Cx("1")
                .Cy("2")
                .R("3")
                .Fill("f")
                .FillOpacity("fo")
                .FillRule("fr")
                .Stroke("st")
                .StrokeWidth("sw")
                .StrokeOpacity("so")
                .StrokeLinecap("slc")
                .StrokeLinejoin("slj")
                .StrokeDasharray("sda")
                .StrokeDashoffset("sdo")
                .Opacity("o")
                .Transform("t")
                .ClipPath("cp")
                .Color("col")
                .Display("d")
                .Visibility("vis")
                .PointerEvents("pe")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());

    [Fact]
    public void A_click_handler_outside_a_live_context_emits_no_handler_attribute() =>
        Assert.Equal("<circle></circle>", Circle.OnClick(() => { }).ToHtml());

    [Fact]
    public void A_click_handler_inside_a_live_context_emits_its_handler_attribute()
    {
        var view = new StubComponent(() => Circle.OnClick(() => { }));
        Assert.Equal("<circle data-rask-on-click=\"h0\"></circle>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void An_async_click_handler_inside_a_live_context_emits_its_handler_attribute()
    {
        var view = new StubComponent(() => Circle.OnClick(async () => { await Task.Yield(); }));
        Assert.Equal("<circle data-rask-on-click=\"h0\"></circle>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Nested_children_render_between_the_open_and_close_tags() =>
        Assert.Equal(
            "<svg viewBox=\"0 0 10 10\"><path d=\"M0 0\"></path><circle r=\"5\"></circle></svg>",
            Svg.ViewBox("0 0 10 10")[SvgPath.D("M0 0"), Circle.R("5")].ToHtml());

    [Fact]
    public void A_shape_can_nest_a_title_child_for_accessibility() =>
        Assert.Equal(
            "<circle r=\"5\"><title>label</title></circle>",
            Circle.R("5")[SvgTitle["label"]].ToHtml());

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<text>&lt;x&gt;</text>", SvgText["<x>"].ToHtml());
}

// Scoped-CSS stamping must flow onto SVG descendants the same way it does for HTML elements.
[Collection("ScopedAssets")]
public partial class SvgScopedCssTests : global::Rask.Core.RaskMarkup
{
    public SvgScopedCssTests()
    {
        ScopedAssetRegistry.InvalidateAll();
        ScopedAssetRegistry.RegisterCss(typeof(SvgCssWrapper), "circle { fill: red; }");
    }

    [Fact]
    public void Serialize_scope_id_stamps_svg_descendants()
    {
        var view = new SvgCssWrapper(Svg[Circle.R("5")]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(SvgCssWrapper));
        Assert.Contains($"<svg data-{scopeId}>", html);
        Assert.Contains($"<circle r=\"5\" data-{scopeId}></circle>", html);
    }

    private sealed class SvgCssWrapper : Component
    {
        private readonly Component _body;
        public SvgCssWrapper(Component body) => _body = body;
        protected override Component? Render() => _body;
    }
}
