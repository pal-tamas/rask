namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsDropdown / BsDropdownItem (live-runtime driven, no JS).
public class BsDropdownTests
{
    [Fact]
    public void Dropdown_Closed_RendersToggleAndHiddenMenu() =>
        Assert.Equal(
            "<div class=\"dropdown\">" +
            "<button class=\"btn btn-primary dropdown-toggle\" aria-expanded=\"false\" type=\"button\">Menu</button>" +
            "<ul class=\"dropdown-menu\">" +
            "<li><a class=\"dropdown-item\" href=\"/a\">A</a></li>" +
            "<li><hr class=\"dropdown-divider\" /></li>" +
            "<li><button class=\"dropdown-item\" type=\"button\">B</button></li>" +
            "</ul></div>",
            BsDropdown(Label: "Menu", Color: BsColor.Primary)[
                BsDropdownItem(Href: "/a")["A"],
                BsDropdownItem(Divider: true),
                BsDropdownItem()["B"]
            ].ToHtml());

    [Fact]
    public void Dropdown_Open_ShowsMenuAndExpandsToggle() =>
        Assert.Equal(
            "<div class=\"dropdown\">" +
            "<button class=\"btn btn-secondary dropdown-toggle\" aria-expanded=\"true\" type=\"button\">Actions</button>" +
            "<ul class=\"dropdown-menu show\"></ul></div>",
            BsDropdown(Label: "Actions", Color: BsColor.Secondary, Open: true).ToHtml());

    [Fact]
    public void Dropdown_AlignEnd_RightAlignsMenu() =>
        Assert.Equal(
            "<div class=\"dropdown\">" +
            "<button class=\"btn btn-primary dropdown-toggle\" aria-expanded=\"false\" type=\"button\">M</button>" +
            "<ul class=\"dropdown-menu dropdown-menu-end\"></ul></div>",
            BsDropdown(Label: "M", Color: BsColor.Primary, AlignEnd: true).ToHtml());

    [Fact]
    public void DropdownItem_Header_RendersH6() =>
        Assert.Equal(
            "<li><h6 class=\"dropdown-header\">Section</h6></li>",
            BsDropdownItem(Header: true)["Section"].ToHtml());

    [Fact]
    public void DropdownItem_ActiveDisabled_ComposeClasses() =>
        Assert.Equal(
            "<li><button class=\"dropdown-item active disabled\" type=\"button\">X</button></li>",
            BsDropdownItem(Active: true, Disabled: true)["X"].ToHtml());
}
