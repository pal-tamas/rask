namespace Rask.UiTests.Components;

/// <summary>
///     The account row — a plain row, or the button that opens the account menu.
/// </summary>
/// <remarks>
///     What makes it one or the other is whether it was given children, because a menu with nothing in it is not
///     a menu. The menu half is <see cref="UiMenuButton" />'s, the same contract <c>UiDropdown</c> renders, so
///     what is worth pinning here is the ROW: the monogram, the name, and the fact that neither is announced
///     twice.
/// </remarks>
public partial class UiProfileTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Without_children_it_is_a_row_and_not_a_button()
    {
        var html = Ui.Profile.Name("Ada Lovelace").ToHtml();

        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
        Assert.DoesNotContain("popovertarget", html, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_children_the_row_is_the_menu_button()
    {
        var html = Ui.Profile.Name("Ada Lovelace")[Ui.MenuItem.Text("Sign out")].ToHtml();

        Assert.Contains("aria-haspopup=\"menu\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("popovertarget=\"uipr-", html, StringComparison.Ordinal);
        Assert.Contains("role=\"menu\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Its_ids_are_its_own_so_a_dropdown_beside_it_cannot_collide()
    {
        // Both draw a menu, and aria-activedescendant names the rows by id — two controls generating the same
        // ids would aim one menu's cursor at the other's rows.
        var html = Div[
            Ui.Profile.Name("Ada Lovelace")[Ui.MenuItem.Text("Sign out")],
            Ui.Dropdown.Trigger("Actions")[Ui.MenuItem.Text("Archive")]
        ].ToHtml();

        Assert.Contains("uipr-", html, StringComparison.Ordinal);
        Assert.Contains("uidd-", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_menu_opens_upward_because_the_row_sits_at_the_bottom()
    {
        // A menu below the account row would open off the bottom of the screen, which is where the row lives.
        Assert.Contains("position-area:block-start",
            Ui.Profile.Name("Ada Lovelace")[Ui.MenuItem.Text("Sign out")].ToHtml(), StringComparison.Ordinal);
        Assert.Contains("position-area:block-end",
            Ui.Profile.Name("Ada Lovelace").Position(Ui.Position.Bottom)[Ui.MenuItem.Text("Sign out")].ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_picture_is_drawn_when_there_is_one()
    {
        var html = Ui.Profile.Name("Ada Lovelace").Avatar("/me.png").ToHtml();

        Assert.Contains("src=\"/me.png\"", html, StringComparison.Ordinal);
        // Decorative: the name is beside it, so announcing the picture too is noise. An EMPTY alt says that; a
        // missing one makes a screen reader read out the file name.
        Assert.Contains("alt=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_picture_it_draws_the_monogram_and_hides_it_from_assistive_tech()
    {
        var html = Ui.Profile.Name("Ada Lovelace").ToHtml();

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("avatar-placeholder", html, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Tamás Pál", "TP")]
    [InlineData("Pál Tamás", "PT")]
    [InlineData("Ada", "A")]
    [InlineData("ada lovelace king", "AL")]
    [InlineData("   ", "?")]
    public void The_monogram_is_the_first_letter_of_the_first_two_words(string name, string expected) =>
        Assert.Equal(expected, global::Rask.UiAvatar.Initials(name));

    [Fact]
    public void The_caption_is_a_second_line_rather_than_part_of_the_name() =>
        Assert.Contains("tamas@example.com",
            Ui.Profile.Name("Ada Lovelace").Caption("tamas@example.com").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void The_words_are_marked_so_a_narrowed_sidebar_can_take_them_away()
    {
        // ui-rail-hide is what Ui.Sidebar.Collapsable's rules key off. Marked by the component that owns the
        // words, because a CSS rule cannot tell a label from content.
        Assert.Contains("ui-rail-hide", Ui.Profile.Name("Ada Lovelace").ToHtml(), StringComparison.Ordinal);
    }
}
