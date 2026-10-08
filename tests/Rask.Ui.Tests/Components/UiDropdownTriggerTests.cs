using System.Text.RegularExpressions;

namespace Rask.UiTests.Components;

/// <summary>
///     A dropdown's trigger that is a kit component rather than an element: the profile row, a navbar item, an
///     avatar drawn as a button. Each wires the button it draws, as the dropdown wires an element it is handed.
/// </summary>
public partial class UiDropdownTriggerTests : global::Rask.Core.RaskMarkup
{
    private static string Button(string html, string marker) =>
        Buttons().Matches(html).Select(match => match.Value).Single(tag => tag.Contains(marker, StringComparison.Ordinal));

    [GeneratedRegex("<button[^>]*>")]
    private static partial Regex Buttons();

    [Fact]
    public void A_dropdown_makes_a_profile_the_button_that_opens_its_menu()
    {
        var html = Ui.Dropdown[Ui.Profile.Name("Ada"), Ui.Menu[Ui.MenuItem["Sign out"]]].ToHtml();

        var button = Button(html, "data-ui-profile");

        Assert.Matches("popovertarget=\"uidd-\\d+-panel\"", button);
        Assert.Contains("aria-haspopup=\"true\"", button, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", button, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropdown_makes_a_navbar_item_the_button_that_opens_its_menu()
    {
        var html = Ui.Dropdown[
            Ui.NavbarItem.IconTrailing(Ui.IconName.ChevronDown)["Account"],
            Ui.Navmenu[Ui.NavmenuItem.Href("/profile")["Profile"]]
        ].ToHtml();

        var button = Button(html, "data-ui-navbar-items");

        Assert.Matches("popovertarget=\"uidd-\\d+-panel\"", button);
        Assert.Contains("aria-haspopup=\"true\"", button, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dropdown_makes_an_avatar_drawn_as_a_button_the_one_that_opens_its_menu()
    {
        var html = Ui.Dropdown[Ui.Avatar.As(Ui.AvatarAs.Button).Name("Ada"), Ui.Menu[Ui.MenuItem["Sign out"]]].ToHtml();

        var button = Button(html, "data-ui-avatar");

        Assert.Matches("popovertarget=\"uidd-\\d+-panel\"", button);
    }

    [Fact]
    public void A_profile_on_its_own_opens_nothing()
    {
        var html = Ui.Profile.Name("Ada").ToHtml();

        Assert.DoesNotContain("popovertarget", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-haspopup", html, StringComparison.Ordinal);
    }
}
