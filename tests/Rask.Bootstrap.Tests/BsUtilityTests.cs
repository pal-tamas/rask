namespace Rask.Bootstrap.Tests;

public class BsUtilityTests
{
    [Fact]
    public void Join_SkipsNullAndEmpty_AndComposes() =>
        Assert.Equal("card shadow-sm border-0", Bs.Join("card", Shadow.Sm, null, "", Border.None));

    [Fact]
    public void Join_AllEmpty_ReturnsNull() => Assert.Null(Bs.Join(null, ""));

    [Fact]
    public void Shadow_And_Border_Tokens() =>
        Assert.Equal("shadow-sm border-0", Bs.Join(Shadow.Sm, Border.None));

    [Fact]
    public void Border_Color() => Assert.Equal("border-primary", Border.Color(BsColor.Primary));

    [Fact]
    public void Margin_NoBreakpoint() => Assert.Equal("mb-4", Margin.Bottom(4));

    [Fact]
    public void Margin_WithBreakpoint() => Assert.Equal("mb-md-4", Margin.Bottom(4, Bp.Md));

    [Fact]
    public void Padding_X_Breakpoint() => Assert.Equal("px-lg-3", Padding.X(3, Bp.Lg));

    [Fact]
    public void Display_Flex_Breakpoint() => Assert.Equal("d-lg-flex", Display.Flex(Bp.Lg));

    [Fact]
    public void Display_None_AllWidths() => Assert.Equal("d-none", Display.None());

    [Fact]
    public void Flex_Justify_Between() => Assert.Equal("justify-content-between", Flex.Justify(BsJustify.Between));

    [Fact]
    public void Flex_Align_Center_Breakpoint() => Assert.Equal("align-items-md-center", Flex.Align(BsAlign.Center, Bp.Md));

    [Fact]
    public void Flex_Gap() => Assert.Equal("gap-2", Flex.Gap(2));

    [Fact]
    public void Txt_Color_Weight_Align()
    {
        Assert.Equal("text-danger", Txt.Color(BsColor.Danger));
        Assert.Equal("text-md-center", Txt.Center(Bp.Md));
        Assert.Equal("fw-bold rounded-pill w-100", Bs.Join(Font.Bold, Rounded.Pill, Sizing.W(100)));
    }

    [Fact]
    public void Sizing_MinViewport()
    {
        Assert.Equal("min-vh-100", Sizing.MinVH100);
        Assert.Equal("min-vw-100", Sizing.MinVW100);
    }
}
