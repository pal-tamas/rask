using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Testing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.RowActions</c>: a list row's actions behind one ⋯, as Flux's table demo draws a row's menu — a ghost,
///     small ⋯ opening a dropdown at the bottom end — in a cell held at the table's right edge.
/// </summary>
public partial class UiRowActionsTests : RaskMarkup
{
    private static readonly RouteUrl ViewOne = new("/suppliers/1");
    private static readonly RouteUrl EditOne = new("/suppliers/1/edit");

    [Fact]
    public void The_menu_is_a_ghost_ellipsis_opening_at_the_bottom_end_in_a_cell_stuck_to_the_right_edge()
    {
        var actions = Ui.RowActions.Edit(EditOne);

        var html = actions.ToHtml().AsText();

        Assert.Matches("^<td class=\"text-end \\[&>\\*\\]:ms-auto z-10 [^\"]*last:sticky last:right-0[^\"]* py-0 \\[:where\\(&\\)\\]:bg-white dark:\\[:where\\(&\\)\\]:bg-zinc-800\" data-ui-cell data-ui-row-actions=\"\">", html);
        Assert.Contains("data-ui-dropdown", html, StringComparison.Ordinal);
        Assert.Contains("inset-inline-end:calc(anchor(end) + -25px)", html, StringComparison.Ordinal);
        Assert.Matches("<button [^>]*aria-haspopup=\"true\"[^>]*>", html);
        Assert.Contains(">Actions</div>", html, StringComparison.Ordinal);
        Assert.Contains("role=\"menu\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void It_holds_view_then_edit_then_what_the_page_adds_then_the_delete_apart_in_red()
    {
        var actions = Ui.RowActions.View(ViewOne).Edit(EditOne).Remove(() => { }, "Delete supplier", "Delete Aminoff Freight?")[
            Ui.MenuItem.Icon(Ui.IconName.ArrowDownTray)["Export"]];

        var html = actions.ToHtml().AsText();

        var view = html.IndexOf("href=\"/suppliers/1\"", StringComparison.Ordinal);
        var edit = html.IndexOf("href=\"/suppliers/1/edit\"", StringComparison.Ordinal);
        var export = html.IndexOf(">Export<", StringComparison.Ordinal);
        var apart = html.IndexOf("data-ui-menu-separator", StringComparison.Ordinal);
        var remove = html.IndexOf("data-rask-confirm=\"Delete Aminoff Freight?\"", StringComparison.Ordinal);
        Assert.True(view > 0 && view < edit && edit < export && export < apart && apart < remove, html);
        Assert.Contains(">View<", html, StringComparison.Ordinal);
        Assert.Contains(">Edit<", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-heading=\"Delete supplier\"", html, StringComparison.Ordinal);
        Assert.Contains(">Delete supplier<", html, StringComparison.Ordinal);
        Assert.Matches("data-rask-key=\"remove\"[^>]*text-red-600|text-red-600[^>]*data-rask-key=\"remove\"", html);
    }

    [Fact]
    public void A_delete_alone_says_the_catalogs_word_with_no_line_above_it()
    {
        var actions = Ui.RowActions.Remove(() => { });

        var html = actions.ToHtml().AsText();

        Assert.DoesNotContain("data-ui-menu-separator", html, StringComparison.Ordinal);
        Assert.Contains(">Delete<", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm=\"Are you sure you want to delete it?\" data-rask-confirm-heading=\"Delete\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_nothing_can_be_done_with_keeps_its_cell_and_has_no_ellipsis()
    {
        var actions = Ui.RowActions;

        var html = actions.ToHtml().AsText();

        Assert.StartsWith("<td ", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
        Assert.EndsWith("></td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hungarian_reader_reads_the_catalogs_words_in_the_menu()
    {
        var actions = () => Ui.RowActions.View(ViewOne).Edit(EditOne).Remove(() => { }).ToHtml().AsText();

        var html = KitCulture.In("hu-HU", actions);

        Assert.Contains(">Műveletek</div>", html, StringComparison.Ordinal);
        Assert.Contains(">Megtekintés<", html, StringComparison.Ordinal);
        Assert.Contains(">Módosítás<", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm=\"Biztos törölni szeretné?\" data-rask-confirm-heading=\"Törlés\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_edit_edits_the_row_where_it_stands_and_wins_over_a_link()
    {
        var edited = new List<string>();
        var page = Page.Render(() => Ui.Table[Ui.TableRows[Ui.TableRow[
            Ui.TableCell["Aminoff Freight"],
            Ui.RowActions.Edit(EditOne).OnEdit(() => edited.Add("Aminoff Freight"))
        ]]]);

        await page.On("[data-rask-key=\"edit\"][role=\"menuitem\"]").Click();

        Assert.Equal(["Aminoff Freight"], edited);
        Assert.DoesNotContain("href=\"/suppliers/1/edit\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Yes_in_the_menu_deletes_its_own_row()
    {
        var page = Page.Render(new SupplierList(keyed: false));

        await page.On("tr:has-text(\"Bothman\") [data-rask-key=\"remove\"][role=\"menuitem\"]").Click();

        Assert.Equal(["Aminoff Freight", "Geidt Logistics"], page.Instance.Suppliers);
    }

    // The apps' report (TA, Eszaf, 2026-11-06): "a page's keys are told apart across all it writes", so their
    // RowActions helper keyed every dropdown and item by the row. A component's key is its WRITER's (docs/composition.md),
    // and Ui.RowActions writes its own dropdown, button and items: each row's are told apart from every other row's.
    [Fact]
    public void Every_rows_menu_is_its_own_without_the_page_keying_anything_in_it()
    {
        using var diagnostics = CapturingDiagnostics.Install();
        var page = Page.Render(new SupplierList(keyed: false));

        page.Render();

        var triggers = page.FindAll("[data-ui-row-actions] button[aria-haspopup]").Select(button => button.Attribute("popovertarget")).ToList();
        var panels = page.FindAll("[data-ui-row-actions] [popover][id^=\"uidd-\"]").Select(panel => panel.Attribute("id")).ToList();
        Assert.Equal(3, triggers.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(triggers, panels);
        Assert.DoesNotContain(diagnostics.OfCategory("Rask.Live"), d => d.Message.Contains("components with Key", StringComparison.Ordinal));
    }

    // The same rows with the items written by the PAGE, as the apps' static helper did: then one key in every row IS one
    // key among everything the page writes, and the runtime says so. Their "row-menu-"+row was the right answer to that.
    [Fact]
    public void Items_the_page_writes_into_every_row_under_one_key_are_one_key_among_everything_the_page_writes()
    {
        using var diagnostics = CapturingDiagnostics.Install();

        _ = Page.Render(new HandWrittenMenus()).Html;

        Assert.Contains(diagnostics.OfCategory("Rask.Live"), d => d.Message.Contains("HandWrittenMenus writes two UiMenuItem components with Key \"edit\"", StringComparison.Ordinal));
    }

    // A delete confirmed in one row while the row above it is deleted: the press reaches the instance it was made on.
    // Ui.RowActions written by the page unkeyed is the n-th of its type the page writes, so it must be keyed by the row,
    // as every component row is (RASK022) — then the late press still deletes its own supplier.
    [Fact]
    public async Task A_late_delete_from_a_keyed_row_still_deletes_its_own_row_after_the_row_above_went()
    {
        var page = Page.Render(new SupplierList(keyed: true));
        var bothman = page.HandlerIdFor("tr:has-text(\"Bothman\") [data-rask-key=\"remove\"][role=\"menuitem\"]", "click");

        await page.On("tr:has-text(\"Aminoff\") [data-rask-key=\"remove\"][role=\"menuitem\"]").Click();
        await page.Invoke(bothman);

        Assert.Equal(["Geidt Logistics"], page.Instance.Suppliers);
    }

    // The hazard the key above guards against, pinned: written unkeyed, the second Ui.RowActions is the second the
    // page writes, so once the first row goes, the press made on Bothman's menu reaches the instance that now draws
    // Geidt's row — and deletes Geidt. RASK022 refuses a component ROW without a key; a component INSIDE a keyed row is
    // not reported, and is reused by its order among everything of its type the page writes (docs/composition.md).
    [Fact]
    public async Task An_unkeyed_row_menu_is_reused_by_its_order_so_a_late_delete_reaches_the_row_now_in_its_place()
    {
        var page = Page.Render(new SupplierList(keyed: false));
        var bothman = page.HandlerIdFor("tr:has-text(\"Bothman\") [data-rask-key=\"remove\"][role=\"menuitem\"]", "click");

        await page.On("tr:has-text(\"Aminoff\") [data-rask-key=\"remove\"][role=\"menuitem\"]").Click();
        await page.Invoke(bothman);

        Assert.Equal(["Bothman & Sons"], page.Instance.Suppliers);
    }

    [Fact]
    public void The_menus_column_is_narrow_at_the_end_stuck_to_the_right_edge_and_named_for_a_screen_reader()
    {
        var column = Ui.TableColumn.Width(Ui.ColumnWidth.RowMenu);

        var html = column.ToHtml().AsText();

        Assert.Matches("^<th class=\"group/end-align z-10 [^\"]*last:sticky last:right-0[^\"]*\\[:where\\(&\\)\\]:w-16 \\[:where\\(&\\)\\]:bg-white dark:\\[:where\\(&\\)\\]:bg-zinc-800", html);
        Assert.Contains("<span class=\"sr-only\">Actions</span>", html, StringComparison.Ordinal);
    }

    private sealed partial class SupplierList(bool keyed) : Component
    {
        public List<string> Suppliers { get; } = ["Aminoff Freight", "Bothman & Sons", "Geidt Logistics"];

        protected override Component? Render() =>
            Ui.Table[Ui.TableRows[Suppliers.Select(name => Ui.TableRow.Key(name)[
                Ui.TableCell[name],
                Actions(name)
            ])]];

        private UiRowActions Actions(string name)
        {
            var actions = Ui.RowActions.Edit(new RouteUrl("/suppliers/" + name.Length)).Remove(() => { Suppliers.Remove(name); }, "Delete supplier");
            return keyed ? actions.Key(name) : actions;
        }
    }

    private sealed partial class HandWrittenMenus : Component
    {
        private static readonly string[] Suppliers = ["Aminoff Freight", "Bothman & Sons"];

        protected override Component? Render() =>
            Ui.Table[Ui.TableRows[Suppliers.Select(name => Ui.TableRow.Key(name)[
                Ui.TableCell[name],
                Ui.TableCell[Ui.Dropdown[Ui.Button.Icon(Ui.IconName.EllipsisHorizontal), Ui.Menu[Ui.MenuItem.Key("edit")["Edit"]]]]
            ])]];
    }
}
