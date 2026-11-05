using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiRowActions" />: a list row's pencil and bin, drawn as Flux draws a row's action — ghost, small and
///     square — in the muted outline the owner approved.
/// </summary>
public partial class UiRowActionsTests : RaskMarkup
{
    private static readonly RouteUrl EditOne = new("/suppliers/1/edit");

    [Fact]
    public void The_pencil_links_to_the_rows_edit_in_the_muted_outline_and_names_itself()
    {
        var actions = Ui.RowActions.Edit(EditOne);

        var html = actions.ToHtml().AsText();

        Assert.StartsWith("<div class=\"[:where(&)]:flex justify-end [:where(&)]:gap-1\" data-ui-row-actions=\"\">", html, StringComparison.Ordinal);
        Assert.Matches("<a [^>]*text-zinc-400 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-white[^>]*href=\"/suppliers/1/edit\"", html);
        Assert.Contains("aria-labelledby=\"ui-tooltip-", html, StringComparison.Ordinal);
        Assert.Contains(">Edit</div>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-confirm", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_puts_a_bin_after_the_pencil_which_asks_its_question_under_its_heading_first()
    {
        var actions = Ui.RowActions.Edit(EditOne).Remove(() => Task.CompletedTask, "Delete supplier", "Delete Aminoff Freight?");

        var html = actions.ToHtml().AsText();

        Assert.True(html.IndexOf("data-rask-key=\"edit\"", StringComparison.Ordinal) < html.IndexOf("data-rask-key=\"remove\"", StringComparison.Ordinal));
        Assert.Matches("<button [^>]*data-rask-key=\"remove\" data-rask-confirm=\"Delete Aminoff Freight\\?\" data-rask-confirm-heading=\"Delete supplier\"", html);
        Assert.Contains(">Delete</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hungarian_reader_reads_the_catalogs_tooltips_and_question()
    {
        var actions = () => Ui.RowActions.Edit(EditOne).Remove(() => { }).ToHtml().AsText();

        var html = KitCulture.In("hu-HU", actions);

        Assert.Contains(">Módosítás</div>", html, StringComparison.Ordinal);
        Assert.Contains(">Törlés</div>", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm=\"Biztos törölni szeretné?\" data-rask-confirm-heading=\"Törlés\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_page_adds_stands_before_the_pencil()
    {
        var actions = Ui.RowActions.Edit(EditOne)[Ui.Button.Key("export").Ghost.Sm.Icon(Ui.IconName.ArrowDownTray)];

        var html = actions.ToHtml();

        Assert.True(html.IndexOf("data-rask-key=\"export\"", StringComparison.Ordinal) < html.IndexOf("data-rask-key=\"edit\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Yes_deletes_the_row_and_a_row_that_cannot_be_deleted_has_no_bin()
    {
        List<string> suppliers = ["Aminoff Freight", "Bothman & Sons"];
        var page = Page.Render(() =>
        [
            Ul[suppliers.Select(name => Li.Key(name)[
                name,
                Ui.RowActions.Edit(new RouteUrl("/suppliers/" + name.Length)).Remove(suppliers.Count > 1 ? () => { suppliers.Remove(name); } : null)
            ])],
        ]);

        await page.On("li:has-text(\"Aminoff\") button[data-rask-confirm]").Click();

        Assert.Equal(["Bothman & Sons"], suppliers);
        Assert.False(page.Exists("[data-rask-confirm]"));
        Assert.True(page.Exists("[data-ui-row-actions] a[href=\"/suppliers/14\"]"));
    }
}
