namespace Rask.UiTests.Components;

/// <summary>
///     Flux's brand, profile and breadcrumbs: what each writes.
/// </summary>
public partial class UiBrandTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_brand_links_home_unless_told_where()
    {
        var home = Ui.Brand.Name("Acme Inc.").ToHtml();

        var elsewhere = Ui.Brand.Name("Acme Inc.").Href("/dashboard").ToHtml();

        Assert.StartsWith("<a ", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/\"", home, StringComparison.Ordinal);
        Assert.Contains("data-ui-brand", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/dashboard\"", elsewhere, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_logo_is_an_image_with_an_empty_alt_beside_the_name()
    {
        var html = Ui.Brand.Name("Acme Inc.").Logo("/logo.png").ToHtml();

        Assert.Contains("<img", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/logo.png\"", html, StringComparison.Ordinal);
        Assert.Contains("alt=\"\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_logo_alone_can_be_named_for_assistive_tech()
    {
        var html = Ui.Brand.Logo("/logo.png").Alt("Acme Inc.").ToHtml();

        Assert.Contains("alt=\"Acme Inc.\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("gap-2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Any_other_logo_is_drawn_as_given_in_a_box_the_call_site_dresses()
    {
        var html = Ui.Brand.Name("Launchpad").Logo(Ui.Icon.Name(Ui.IconName.RocketLaunch).Micro).LogoClass("bg-cyan-500").ToHtml();

        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.Contains("bg-cyan-500", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_is_a_button_holding_a_small_avatar_and_a_chevron()
    {
        var html = Ui.Profile.Avatar("/me.png").ToHtml();

        Assert.StartsWith("<button ", html, StringComparison.Ordinal);
        Assert.Contains("type=\"button\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-profile", html, StringComparison.Ordinal);
        Assert.Contains("data-size=\"sm\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/me.png\"", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("<svg").Length - 1);
    }

    [Fact]
    public void A_profile_with_no_picture_shows_initials_from_its_name()
    {
        var named = Ui.Profile.Name("Caleb Porzio").ToHtml();

        var unnamed = Ui.Profile.AvatarName("Caleb Porzio").AvatarColor(Ui.Color.Cyan).ToHtml();

        Assert.Contains(">CP</span>", named, StringComparison.Ordinal);
        Assert.Contains(">Caleb Porzio</span>", named, StringComparison.Ordinal);
        Assert.Contains(">CP</span>", unnamed, StringComparison.Ordinal);
        Assert.DoesNotContain("Caleb Porzio", unnamed, StringComparison.Ordinal);
        Assert.Contains("bg-cyan-200", unnamed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_chevron_can_be_dropped_or_swapped()
    {
        var bare = Ui.Profile.Avatar("/me.png").Chevron(false).ToHtml();

        var swapped = Ui.Profile.Avatar("/me.png").Chevron(false).IconTrailing(Ui.IconName.ChevronUpDown).ToHtml();

        Assert.DoesNotContain("<svg", bare, StringComparison.Ordinal);
        Assert.Contains("<svg", swapped, StringComparison.Ordinal);
    }

    [Fact]
    public void A_circle_profile_rounds_the_button_and_the_avatar()
    {
        var html = Ui.Profile.Circle().Avatar("/me.png").ToHtml();

        Assert.Contains("rounded-full", html[..html.IndexOf('>', StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("data-circle=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_can_hold_an_avatar_of_its_own()
    {
        var html = Ui.Profile.Avatar(Ui.Avatar.Lg.Icon(Ui.IconName.User)).ToHtml();

        Assert.Contains("data-size=\"lg\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-size=\"sm\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Breadcrumbs_are_a_marked_row_of_items()
    {
        var html = Ui.Breadcrumbs[Ui.BreadcrumbsItem.Href("/")["Home"], Ui.BreadcrumbsItem["Post"]].ToHtml();

        Assert.StartsWith("<div class=\"flex\" data-ui-breadcrumbs>", html, StringComparison.Ordinal);
        Assert.Equal(2, html.Split("data-ui-breadcrumbs-item").Length - 1);
    }

    [Fact]
    public void An_item_with_an_href_is_a_link_and_one_without_is_text()
    {
        var link = Ui.BreadcrumbsItem.Href("/blog")["Blog"].ToHtml();

        var text = Ui.BreadcrumbsItem["Post"].ToHtml();

        Assert.Contains("<a ", link, StringComparison.Ordinal);
        Assert.Contains("href=\"/blog\"", link, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_separator_is_a_chevron_for_each_reading_direction()
    {
        var html = Ui.BreadcrumbsItem.Href("/")["Home"].ToHtml();

        Assert.Equal(2, html.Split("<svg").Length - 1);
        Assert.Contains("rtl:hidden", html, StringComparison.Ordinal);
        Assert.Contains("hidden rtl:inline", html, StringComparison.Ordinal);
        Assert.Contains("group-last/breadcrumb:hidden", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_separator_is_one_icon()
    {
        var html = Ui.BreadcrumbsItem.Href("/").Separator(Ui.IconName.Slash)["Home"].ToHtml();

        Assert.Equal(1, html.Split("<svg").Length - 1);
    }

    [Fact]
    public void An_icon_can_stand_in_for_an_items_words()
    {
        var html = Ui.BreadcrumbsItem.Href("/").Icon(Ui.IconName.Home).ToHtml();

        Assert.Equal(3, html.Split("<svg").Length - 1);
        Assert.Contains("viewBox=\"0 0 20 20\"", html, StringComparison.Ordinal);
    }
}
