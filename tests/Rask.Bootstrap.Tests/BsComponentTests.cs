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
    public void Modal_Fullscreen_AddsFullscreenClass() =>
        Assert.Contains(
            "<div class=\"modal-dialog modal-fullscreen\">",
            BsModal(Open: true, Title: "Hi", Fullscreen: true)["body"].ToHtml());

    [Fact]
    public void Modal_FullscreenBelow_AddsBreakpointDownClassAndComposesWithSize() =>
        Assert.Contains(
            "<div class=\"modal-dialog modal-fullscreen-sm-down modal-lg\">",
            BsModal(Open: true, Title: "Hi", FullscreenBelow: Bp.Sm, Size: BsSize.Lg)["body"].ToHtml());

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

    [Fact]
    public void Navbar_Default_WrapsChildrenInContainer() =>
        Assert.Equal(
            "<nav class=\"navbar\"><div class=\"container-fluid\"><span>x</span></div></nav>",
            BsNavbar()[Span()["x"]].ToHtml());

    [Fact]
    public void Navbar_ColorThemeStickyExpand_NoContainer_ComposesClasses() =>
        Assert.Equal(
            "<nav class=\"navbar navbar-expand-lg bg-dark sticky-top\" data-bs-theme=\"dark\">x</nav>",
            BsNavbar(Color: BsColor.Dark, Theme: BsTheme.Dark, Sticky: true, Expand: Bp.Lg, Container: false)["x"]
                .ToHtml());

    [Fact]
    public void Nav_Vertical_StacksWithFlexColumn() =>
        Assert.Equal("<ul class=\"nav flex-column\"></ul>", BsNav(Vertical: true).ToHtml());

    [Fact]
    public void Nav_PillsFill_ComposesClasses() =>
        Assert.Equal("<ul class=\"nav nav-pills nav-fill\"></ul>", BsNav(Pills: true, Fill: true).ToHtml());

    [Fact]
    public void NavItem_WithHref_RendersSpaRoutedNavLink() =>
        Assert.Equal(
            "<li class=\"nav-item\"><a class=\"nav-link\" href=\"/x\" data-rask-nav>Tags</a></li>",
            BsNavItem(Href: "/x")["Tags"].ToHtml());

    [Fact]
    public void NavItem_WithoutHref_RendersPlainSpan() =>
        Assert.Equal(
            "<li class=\"nav-item\"><span class=\"nav-link disabled\">Soon</span></li>",
            BsNavItem(Disabled: true)["Soon"].ToHtml());

    [Fact]
    public void Offcanvas_Default_IsAlwaysADrawer() =>
        Assert.Equal(
            "<div class=\"offcanvas offcanvas-start\" role=\"dialog\" tabindex=\"-1\">"
            + "<div class=\"offcanvas-body\">x</div></div>",
            BsOffcanvas(HideClose: true)["x"].ToHtml());

    [Fact]
    public void Offcanvas_Responsive_EmitsBreakpointBaseClass_AndHidesChromeAbove()
    {
        var html = BsOffcanvas(Responsive: Bp.Md, Title: "Menu")["x"].ToHtml();
        // Drawer below md, static at/above md.
        Assert.Contains("class=\"offcanvas-md offcanvas-start\"", html);
        // The header carries d-md-none so the static desktop panel shows no drawer chrome.
        Assert.Contains("class=\"offcanvas-header d-md-none\"", html);
        Assert.Contains("class=\"offcanvas-body\"", html);
    }

    [Fact]
    public void Offcanvas_Responsive_Open_HidesBackdropAboveBreakpoint()
    {
        var html = BsOffcanvas(Responsive: Bp.Md, Open: true, HideClose: true)["x"].ToHtml();
        Assert.Contains("class=\"offcanvas-md offcanvas-start show\"", html);
        Assert.Contains("class=\"offcanvas-backdrop fade show d-md-none\"", html);
    }
}
