namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for the layout primitives: BsContainer / BsRow / BsCol / BsStack.
public partial class BsLayoutTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Container_CentresAndCapsWidth() =>
        Assert.Equal("<div class=\"container\">x</div>", BsContainer["x"].ToHtml());

    [Fact]
    public void Container_Fluid_SpansFullWidth() =>
        Assert.Equal("<div class=\"container-fluid\"></div>", BsContainer.Fluid(true).ToHtml());

    [Fact]
    // .container-md is fluid *below* md and capped from md up — hence the prop name.
    public void Container_FluidBelow_EmitsTheBreakpointContainer() =>
        Assert.Equal("<div class=\"container-md\"></div>", BsContainer.FluidBelow(Bp.Md).ToHtml());

    [Fact]
    // FluidBelow is the more specific ask, so it wins — mirroring BsModal's FullscreenBelow over Fullscreen.
    public void Container_FluidBelow_SupersedesFluid() =>
        Assert.Equal("<div class=\"container-lg\"></div>",
            BsContainer.Fluid(true).FluidBelow(Bp.Lg).ToHtml());

    [Fact]
    public void Row_WrapsColumns() =>
        Assert.Equal("<div class=\"row\"><div class=\"col\"></div></div>", BsRow[BsCol].ToHtml());

    [Fact]
    public void Row_Gutter_SpacesColumnsOnBothAxes() =>
        Assert.Equal("<div class=\"row g-3\"></div>", BsRow.Gutter(3).ToHtml());

    [Fact]
    // Gutter 0 collapses the gutters — a real ask, and the reason Gutter is int? rather than a truthy flag.
    public void Row_GutterZero_CollapsesGutters() =>
        Assert.Equal("<div class=\"row g-0\"></div>", BsRow.Gutter(0).ToHtml());

    [Fact]
    public void Col_NoSpan_IsEqualWidth() =>
        Assert.Equal("<div class=\"col\"></div>", BsCol.ToHtml());

    [Fact]
    public void Col_Auto_SizesToContent() =>
        Assert.Equal("<div class=\"col-auto\">x</div>", BsCol.Auto(true)["x"].ToHtml());

    [Fact]
    // A column with a span deliberately omits a companion .col: `.row > *` is already width:100% below the
    // breakpoint, whereas `col col-md-6` would be equal-width there instead — a different layout.
    public void Col_Breakpoint_EmitsOnlyTheSpan() =>
        Assert.Equal("<div class=\"col-md-6\"></div>", BsCol.Md(6).ToHtml());

    [Fact]
    public void Col_Span_EmitsTheUnprefixedSpan() =>
        Assert.Equal("<div class=\"col-7\"></div>", BsCol.Span(7).ToHtml());

    [Fact]
    // Spans stack across breakpoints exactly as the class names do, narrowest first.
    public void Col_StackedSpans_EmitInBreakpointOrder() =>
        Assert.Equal("<div class=\"col-7 col-sm-8 col-md-6 col-lg-4 col-xl-3 col-xxl-2\"></div>",
            BsCol.Span(7).Sm(8).Md(6).Lg(4).Xl(3).Xxl(2).ToHtml());

    [Fact]
    // Auto and Span fill the same unprefixed slot, so Auto wins rather than both being emitted: `col-auto
    // col-7` would put two equal-specificity rules on one element and .col-7 is later in the stylesheet,
    // so the column would silently ignore Auto with nothing in the markup to show why.
    public void Col_AutoWithSpan_PrefersAutoOverTheConflictingSpan() =>
        Assert.Equal("<div class=\"col-auto\"></div>", BsCol.Auto(true).Span(7).ToHtml());

    [Fact]
    // Auto plus a *breakpoint* span is a different thing and stacks normally — content-width below md,
    // half from md up. This is the combination the Auto/Span rule above must not break.
    public void Col_AutoWithBreakpointSpan_Stacks() =>
        Assert.Equal("<div class=\"col-auto col-md-6\"></div>", BsCol.Auto(true).Md(6).ToHtml());

    [Fact]
    public void Col_UserClass_ComesLast() =>
        Assert.Equal("<div class=\"col-md-6 text-center\"></div>",
            BsCol.Md(6).Class(Txt.Center()).ToHtml());

    [Fact]
    // Horizontal emits no flex-row token — row is the flex default, which keeps this byte-identical to the
    // "d-flex gap-2" it replaces at a call site.
    public void Stack_IsHorizontalByDefault() =>
        Assert.Equal("<div class=\"d-flex gap-2\">x</div>", BsStack.Gap(2)["x"].ToHtml());

    [Fact]
    public void Stack_Vertical_StacksIntoAColumn() =>
        Assert.Equal("<div class=\"d-flex flex-column gap-3\"></div>",
            BsStack.Vertical(true).Gap(3).ToHtml());

    [Fact]
    // Bare BsStack() is a plain d-flex — no gap token, so it doesn't invent spacing the caller didn't ask for.
    public void Stack_NoGap_IsAPlainFlexRow() =>
        Assert.Equal("<div class=\"d-flex\"></div>", BsStack.ToHtml());

    [Fact]
    // This is what .hstack means, said out loud. BsStack builds on d-flex precisely so that centring is an
    // explicit opt-in rather than a silent default.
    public void Stack_Align_CentresItemsOnTheCrossAxis() =>
        Assert.Equal("<div class=\"d-flex gap-2 align-items-center\"></div>",
            BsStack.Gap(2).Align(BsAlign.Center).ToHtml());

    [Fact]
    public void Stack_Justify_SpacesItemsOnTheMainAxis() =>
        Assert.Equal("<div class=\"d-flex justify-content-between\"></div>",
            BsStack.Justify(BsJustify.Between).ToHtml());

    [Fact]
    public void Stack_WrapItems_LetsItemsFlowOntoMoreLines() =>
        Assert.Equal("<div class=\"d-flex gap-2 flex-wrap\"></div>",
            BsStack.Gap(2).WrapItems(true).ToHtml());

    [Fact]
    // Token order is fixed: d-flex, direction, gap, justify, align, wrap, then the user's Class.
    public void Stack_AllTokens_RenderInAStableOrder() =>
        Assert.Equal(
            "<div class=\"d-flex flex-column gap-1 justify-content-evenly align-items-baseline flex-wrap mb-2\"></div>",
            BsStack
                .Vertical(true)
                .Gap(1)
                .Justify(BsJustify.Evenly)
                .Align(BsAlign.Baseline)
                .WrapItems(true)
                .Class(Margin.Bottom(2)).ToHtml());

    [Fact]
    // Responsive direction rides on Class — Bootstrap ships no .vstack/.hstack breakpoint variant, so this
    // composes only because the base is d-flex.
    public void Stack_ResponsiveDirection_ComposesViaClass() =>
        Assert.Equal("<div class=\"d-flex flex-column gap-3 flex-md-row\"></div>",
            BsStack.Vertical(true).Gap(3).Class(Flex.Row(Bp.Md)).ToHtml());

    [Fact]
    public void Layout_IdAndClass_FlowThrough() =>
        Assert.Equal("<div id=\"grid\" class=\"row g-4 mb-3\"></div>",
            BsRow.Gutter(4).Id("grid").Class(Margin.Bottom(3)).ToHtml());

    [Fact]
    // The shape the migration is built on: container > row > col, nested.
    public void Layout_ComposesIntoAPageShell() =>
        Assert.Equal(
            "<div class=\"container\"><div class=\"row g-4\">"
            + "<div class=\"col-md-6\">left</div><div class=\"col-md-6\">right</div>"
            + "</div></div>",
            BsContainer[BsRow.Gutter(4)[BsCol.Md(6)["left"], BsCol.Md(6)["right"]]].ToHtml());
}
