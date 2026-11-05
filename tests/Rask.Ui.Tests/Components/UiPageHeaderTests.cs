using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiPageHeader" />: the row a page opens with — its <c>&lt;h1&gt;</c>, a line under it, the actions at the
///     end and the trail above — laid out as Flux's demos lay out a heading and its actions.
/// </summary>
public partial class UiPageHeaderTests : RaskMarkup
{
    [Fact]
    public void A_heading_alone_is_the_pages_h1_in_the_extra_large_size_with_room_under_the_row()
    {
        var header = Ui.PageHeader.Heading("Suppliers");

        var html = header.ToHtml().AsText();

        Assert.StartsWith("<div class=\"[:where(&)]:mb-6\" data-ui-page-header=\"\"><div class=\"flex flex-wrap items-center justify-between gap-2\"><div class=\"min-w-0 max-sm:w-full\"><h1 ", html, StringComparison.Ordinal);
        Assert.Matches("<h1 class=\"[^\"]*\\[:where\\(&\\)\\]:text-2xl[^\"]*\" data-ui-heading>Suppliers</h1>", html);
        Assert.DoesNotContain("data-ui-subheading>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("justify-end", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Under_a_title_of_the_pages_own_the_heading_takes_the_level_it_is_given()
    {
        var header = Ui.PageHeader.Heading("Suppliers").Level(2);

        var html = header.ToHtml().AsText();

        Assert.Contains("data-ui-heading>Suppliers</h2>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<h1", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_subheading_is_the_muted_line_under_the_heading()
    {
        var header = Ui.PageHeader.Heading("Suppliers").Subheading("Everyone you buy from.");

        var html = header.ToHtml().AsText();

        Assert.Matches("Suppliers</h1><div class=\"[^\"]*text-zinc-500[^\"]*\" data-ui-subheading>Everyone you buy from.</div></div>", html);
    }

    [Fact]
    public void The_actions_keep_to_the_end_of_the_row_and_go_under_the_title_on_a_phone()
    {
        var header = Ui.PageHeader.Heading("Suppliers")[Ui.Button.Key("export")["Export"], Ui.Button.Key("new").Primary["New supplier"]];

        var html = header.ToHtml().AsText();

        Assert.Contains("<div class=\"min-w-0 max-sm:w-full max-sm:mb-2\"><h1 ", html, StringComparison.Ordinal);
        Assert.Matches("</h1></div><div class=\"flex flex-1 flex-wrap items-center justify-end gap-2\"><button [^>]*data-rask-key=\"export\"", html);
        Assert.True(html.IndexOf("data-rask-key=\"export\"", StringComparison.Ordinal) < html.IndexOf("data-rask-key=\"new\"", StringComparison.Ordinal));
    }

    [Fact]
    public void The_trail_stands_above_the_title()
    {
        var header = Ui.PageHeader.Heading("Aminoff Freight")
            .Breadcrumbs(Ui.Breadcrumbs[Ui.BreadcrumbsItem.Href("/suppliers")["Suppliers"], Ui.BreadcrumbsItem["Aminoff Freight"]]);

        var html = header.ToHtml().AsText();

        Assert.True(html.IndexOf("data-ui-breadcrumbs", StringComparison.Ordinal) < html.IndexOf("<h1 ", StringComparison.Ordinal));
        Assert.Contains("data-ui-page-header=\"\"><div class=\"mb-2\"><div class=\"[:where(&)]:flex\" data-ui-breadcrumbs>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_of_the_call_sites_is_added_to_the_headers_spacing_which_weighs_nothing()
    {
        var header = Ui.PageHeader.Heading("Suppliers").Class("mb-0");

        var html = header.ToHtml().AsText();

        Assert.StartsWith("<div class=\"[:where(&)]:mb-6 mb-0\" data-ui-page-header=\"\">", html, StringComparison.Ordinal);
    }
}
