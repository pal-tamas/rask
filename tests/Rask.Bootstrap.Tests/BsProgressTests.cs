namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsProgress. Bootstrap 5.3 carries role/aria on the outer .progress and
// the width on the inner .progress-bar — these lock that shape.
public class BsProgressTests
{
    [Fact]
    public void Progress_Default_EmitsRoleAndAriaOnOuter() =>
        Assert.Equal(
            "<div class=\"progress\" role=\"progressbar\" aria-valuenow=\"60\" aria-valuemin=\"0\" " +
            "aria-valuemax=\"100\"><div class=\"progress-bar\" style=\"width:60%\"></div></div>",
            BsProgress(Value: 60).ToHtml());

    [Fact]
    public void Progress_Label_RendersInsideBar() =>
        Assert.Equal(
            "<div class=\"progress\" role=\"progressbar\" aria-valuenow=\"50\" aria-valuemin=\"0\" " +
            "aria-valuemax=\"100\"><div class=\"progress-bar\" style=\"width:50%\">50%</div></div>",
            BsProgress(Value: 50, Label: "50%").ToHtml());

    [Fact]
    public void Progress_ColorStripedAnimated_StackModifiers() =>
        Assert.Equal(
            "<div class=\"progress\" role=\"progressbar\" aria-valuenow=\"75\" aria-valuemin=\"0\" " +
            "aria-valuemax=\"100\"><div class=\"progress-bar progress-bar-striped progress-bar-animated " +
            "bg-success\" style=\"width:75%\"></div></div>",
            BsProgress(Value: 75, Color: BsColor.Success, Striped: true, Animated: true).ToHtml());

    [Fact]
    // Custom Min/Max scale the fill: 3 on a 1..5 scale is (3-1)/(5-1) = 50%.
    public void Progress_CustomRange_ScalesWidthAndAria() =>
        Assert.Equal(
            "<div class=\"progress\" role=\"progressbar\" aria-valuenow=\"3\" aria-valuemin=\"1\" " +
            "aria-valuemax=\"5\"><div class=\"progress-bar\" style=\"width:50%\"></div></div>",
            BsProgress(Value: 3, Min: 1, Max: 5).ToHtml());
}
