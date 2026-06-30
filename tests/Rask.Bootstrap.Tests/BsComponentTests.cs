namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for the Bootstrap components. ToHtml() renders a component to its static
// markup (no live context), which is all these class/structure checks need.
public class BsComponentTests
{
    [Fact]
    public void Button_ColorAndSize_ComposesBtnClasses() =>
        Assert.Equal(
            "<button class=\"btn btn-primary btn-lg\" type=\"button\">Save</button>",
            BsButton(Color: BsColor.Primary, Size: BsSize.Lg)["Save"].ToHtml());

    [Fact]
    public void Button_Outline_UsesOutlineVariant() =>
        Assert.Equal(
            "<button class=\"btn btn-outline-danger\" type=\"button\">X</button>",
            BsButton(Color: BsColor.Danger, Outline: true)["X"].ToHtml());

    [Fact]
    public void Button_MergesUserClass() =>
        Assert.Equal(
            "<button class=\"btn btn-secondary w-100\" type=\"button\">Wide</button>",
            BsButton(Color: BsColor.Secondary, Class: "w-100")["Wide"].ToHtml());

    [Fact]
    public void Badge_UsesContrastAwareTextBg() =>
        Assert.Equal(
            "<span class=\"badge text-bg-success\">New</span>",
            BsBadge(Color: BsColor.Success)["New"].ToHtml());

    [Fact]
    public void Badge_Pill_AddsRoundedPill() =>
        Assert.Equal(
            "<span class=\"badge text-bg-info rounded-pill\">9</span>",
            BsBadge(Color: BsColor.Info, Pill: true)[9].ToHtml());

    [Fact]
    public void Alert_RendersRoleAndColor() =>
        Assert.Equal(
            "<div class=\"alert alert-warning\" role=\"alert\">Careful</div>",
            BsAlert(Color: BsColor.Warning)["Careful"].ToHtml());

    [Fact]
    public void Alert_Dismissible_AppendsCloseButton() =>
        Assert.Equal(
            "<div class=\"alert alert-danger alert-dismissible\" role=\"alert\">Boom"
            + "<button class=\"btn-close\" aria-label=\"Close\" type=\"button\"></button></div>",
            BsAlert(Color: BsColor.Danger, Dismissible: true)["Boom"].ToHtml());

    [Fact]
    public void Spinner_DefaultBorder_HasRoleStatusAndHiddenLabel() =>
        Assert.Equal(
            "<div class=\"spinner-border\" role=\"status\"><span class=\"visually-hidden\">Loading&#x2026;</span></div>",
            BsSpinner().ToHtml());

    [Fact]
    public void Card_NestsSections() =>
        Assert.Equal(
            "<div class=\"card\"><div class=\"card-body\"><h5 class=\"card-title\">T</h5>"
            + "<p class=\"card-text\">B</p></div></div>",
            BsCard()[BsCardBody()[BsCardTitle()["T"], BsCardText()["B"]]].ToHtml());

    [Fact]
    public void Progress_ClampsAndWritesWidthAndAria() =>
        Assert.Equal(
            "<div class=\"progress\" role=\"progressbar\" aria-valuenow=\"50\" aria-valuemin=\"0\" aria-valuemax=\"100\">"
            + "<div class=\"progress-bar\" style=\"width:50%\"></div></div>",
            BsProgress(Value: 50).ToHtml());

    [Fact]
    public void Modal_Closed_RendersNothing() =>
        Assert.Equal("", BsModal(Title: "Hi")["body"].ToHtml());

    [Fact]
    public void Modal_Open_RendersDialogAndBackdrop() =>
        Assert.Equal(
            "<div class=\"modal fade show\" style=\"display:block\" role=\"dialog\" tabindex=\"-1\" aria-modal=\"true\">"
            + "<div class=\"modal-dialog\"><div class=\"modal-content\">"
            + "<div class=\"modal-header\"><h5 class=\"modal-title\">Hi</h5>"
            + "<button class=\"btn-close\" aria-label=\"Close\" type=\"button\"></button></div>"
            + "<div class=\"modal-body\">body</div></div></div></div>"
            + "<div class=\"modal-backdrop fade show\"></div>",
            BsModal(Open: true, Title: "Hi")["body"].ToHtml());

    [Fact]
    public void Icon_RendersBiClassesAndHiddenByDefault() =>
        Assert.Equal(
            "<i class=\"bi bi-heart-fill\" aria-hidden=\"true\"></i>",
            BsIcon(Name: BsIconName.HeartFill).ToHtml());

    [Fact]
    public void Collapse_TogglesShow()
    {
        Assert.Equal("<div class=\"collapse\">x</div>", BsCollapse()["x"].ToHtml());
        Assert.Equal("<div class=\"collapse show\">x</div>", BsCollapse(Open: true)["x"].ToHtml());
    }
}
