using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The kit's list as a list page writes it — <c>Ui.Table</c> sorted in the address, each row's ⋯ menu with an edit
///     link and a <c>MenuItem.Confirm</c>, <c>Ui.EditorDialog.In("edit")</c> over it, <c>Ui.Confirm</c> beside it — on a
///     desk and on a phone. A dialog the reader closed stays closed: through a while left alone, through a press the
///     host answers, through the tab hidden and shown again; and closing the editor takes out of the address only its
///     own key.
/// </summary>
/// <remarks>
///     Found in two apps as an editor or a question that came back open after it was closed: each app wrote its own
///     browser test for it, and these are those tests on the kit itself.
/// </remarks>
public sealed class EditorDialogStaysClosedTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Editor = "dialog[data-rask-modal-open]";

    private const string Question = "#ui-confirm";

    private const string Hidden =
        """
        () => {
            Object.defineProperty(document, "visibilityState", {configurable: true, get: () => "hidden"});
            Object.defineProperty(document, "hidden", {configurable: true, get: () => true});
            document.dispatchEvent(new Event("visibilitychange"));
        }
        """;

    private const string Visible =
        """
        () => {
            Object.defineProperty(document, "visibilityState", {configurable: true, get: () => "visible"});
            Object.defineProperty(document, "hidden", {configurable: true, get: () => false});
            document.dispatchEvent(new Event("visibilitychange"));
        }
        """;

    [Theory]
    [InlineData("editor by the backdrop", 1280)]
    [InlineData("editor by the backdrop", 390)]
    [InlineData("question by Escape", 1280)]
    [InlineData("question by Escape", 390)]
    [InlineData("question by No", 1280)]
    [InlineData("question by No", 390)]
    public async Task A_dialog_the_reader_closed_stays_closed_through_a_wait_a_sort_and_the_tab_coming_back(string way, int width)
    {
        await using var session = await OpenAsync(width, "/suppliers");
        var page = session.Page;
        await CloseAsync(page, way);

        await page.WaitForTimeoutAsync(2000);
        var openAfterAWait = await OpenDialogsAsync(page);
        await page.Locator("th button", new() { HasText = "City" }).ClickAsync();
        await Expect(page.Locator("#suppliers tbody tr td:first-child")).ToHaveTextAsync(["Bothman & Sons", "Aminoff Freight", "Geidt Logistics", "Levin Oil"]);
        await page.WaitForTimeoutAsync(500);
        var openAfterASort = await OpenDialogsAsync(page);
        await page.EvaluateAsync(Hidden);
        await page.WaitForTimeoutAsync(300);
        await page.EvaluateAsync(Visible);
        await page.WaitForTimeoutAsync(1000);
        var openAfterTheTabCameBack = await OpenDialogsAsync(page);

        Assert.Equal(0, openAfterAWait);
        Assert.Equal(0, openAfterASort);
        Assert.Equal(0, openAfterTheTabCameBack);
        Assert.DoesNotContain("edit=", page.Url, StringComparison.Ordinal);
        await Expect(page.Locator("#archived")).ToHaveTextAsync("archived=0");
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task Escape_on_an_editor_opened_at_a_sorted_address_takes_out_the_edit_key_and_keeps_the_sort(int width)
    {
        await using var session = await OpenAsync(width, "/suppliers?sort=city&dir=desc&edit=2");
        var page = session.Page;
        await Expect(page.Locator(Editor)).ToBeVisibleAsync();
        await Expect(page.Locator("#name")).ToHaveValueAsync("Bothman & Sons");

        await page.FocusAsync("#name");
        await page.Keyboard.PressAsync("Escape");

        await Expect(page.Locator(Editor)).ToBeHiddenAsync();
        await Expect(page).ToHaveURLAsync(session.BaseUrl + "/suppliers?sort=city&dir=desc");
        await Expect(page.Locator("#suppliers tbody tr td:first-child")).ToHaveTextAsync(["Levin Oil", "Geidt Logistics", "Aminoff Freight", "Bothman & Sons"]);
    }

    private async Task<HookSession> OpenAsync(int width, string path)
    {
        var session = await HookSession.OpenAsync<SupplierListApp>(
            playwright, new() { ViewportSize = new() { Width = width, Height = 800 } }, path: path);
        await session.HooksLoadedAsync();
        await Expect(session.Page.Locator("#suppliers tbody tr")).ToHaveCountAsync(4);
        return session;
    }

    private static async Task CloseAsync(IPage page, string way)
    {
        var row = page.Locator("#suppliers tbody tr", new() { HasText = "Geidt Logistics" });
        await row.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Actions" }).ClickAsync();
        if (way == "editor by the backdrop")
        {
            await row.GetByRole(Microsoft.Playwright.AriaRole.Menuitem, new() { Name = "Edit" }).ClickAsync();
            await Expect(page.Locator(Editor)).ToBeVisibleAsync();
            await Expect(page.Locator("#name")).ToHaveValueAsync("Geidt Logistics");
            await Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("edit=3"));
            await PressOutsideAsync(page);
            await Expect(page.Locator(Editor)).ToBeHiddenAsync();
            return;
        }

        await row.GetByRole(Microsoft.Playwright.AriaRole.Menuitem, new() { Name = "Archive" }).ClickAsync();
        await Expect(page.Locator(Question)).ToBeVisibleAsync();
        if (way == "question by Escape")
        {
            await page.Keyboard.PressAsync("Escape");
        }
        else
        {
            await page.ClickAsync(Question + " [data-rask-confirm-dialog=no]");
        }

        await Expect(page.Locator(Question)).ToBeHiddenAsync();
    }

    // The backdrop: halfway between the editor's panel and the viewport's bottom edge, or its top one if it is nearer.
    private static async Task PressOutsideAsync(IPage page)
    {
        var at = await page.Locator(Editor).EvaluateAsync<double[]>(
            """
            d => {
                const panel = [...d.querySelectorAll('*')].map(e => e.getBoundingClientRect()).filter(r => r.width > 0)
                    .reduce((a, r) => ({ top: Math.min(a.top, r.top), bottom: Math.max(a.bottom, r.bottom) }), { top: innerHeight, bottom: 0 });
                const below = innerHeight - panel.bottom, above = panel.top;
                return [innerWidth / 2, below >= above ? panel.bottom + below / 2 : above / 2];
            }
            """);
        await page.Mouse.ClickAsync((float)at[0], (float)at[1]);
    }

    private static Task<int> OpenDialogsAsync(IPage page) => page.EvaluateAsync<int>("() => document.querySelectorAll('dialog[open]').length");
}

/// <summary>The app as a list page's layout is: a router, and the kit's question beside it.</summary>
public sealed partial class SupplierListApp : Component
{
    protected override Component? HeadAssets => Markup.Title["supplier list"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Router,
        Ui.Confirm
    ];
}

/// <summary>
///     The suppliers, sorted in the address (<c>?sort=</c>, <c>?dir=</c>), each row's ⋯ holding Edit — opening the editor
///     over the list at <c>?edit=</c> — and an Archive that asks first.
/// </summary>
[Route("/suppliers")]
public sealed partial class SupplierListPage(RouteState route) : Component
{
    private static readonly SupplierListRow[] Suppliers =
    [
        new(1, "Aminoff Freight", "Oslo"), new(2, "Bothman & Sons", "Leeds"), new(3, "Geidt Logistics", "Porto"), new(4, "Levin Oil", "Tartu"),
    ];

    private SupplierListRow _draft = new(0, "", "");
    private string? _drafted;
    private int _archived;

    [QueryParam("sort")] public string? Sort { get; set; }

    [QueryParam("dir")] public string? Dir { get; set; }

    [QueryParam("edit")] public string? Edit { get; set; }

    protected override Component? Render() =>
        Main.Class("space-y-4 p-4")[
            P.Id("archived")["archived=" + _archived.ToString(CultureInfo.InvariantCulture)],
            Ui.Table.Id("suppliers").SortIn("sort", "dir")[
                Ui.TableColumns[
                    Ui.TableColumn.Key("name").Sortable()["Supplier"],
                    Ui.TableColumn.Key("city").Width(Ui.ColumnWidth.Reference).Sortable()["City"],
                    Ui.TableColumn.RowMenu
                ],
                Ui.TableRows[
                    Sorted().Select(supplier => Ui.TableRow.Key(supplier.Id)[
                        Ui.TableCell.Cut(supplier.Name),
                        Ui.TableCell.Cut(supplier.City),
                        Ui.RowActions.Key(supplier.Id).Edit(route.With("edit", supplier.Id.ToString(CultureInfo.InvariantCulture)))[
                            Ui.MenuItem.OnClick(() => _archived++).Confirm($"Archive {supplier.Name}?")["Archive"]
                        ]
                    ])
                ]
            ],
            Ui.EditorDialog.In("edit").Heading("Edit supplier")[
                Form.Model(Drafted()).OnSubmit(_ => Go.Without("edit")).Class("space-y-6")[
                    Ui.Input.Bind(() => _draft.Name).Id("name").Label("Name"),
                    Ui.FormButtons.Back(route.With("edit", null))
                ]
            ]
        ];

    private IEnumerable<SupplierListRow> Sorted()
    {
        Func<SupplierListRow, string> by = string.Equals(Sort, "city", StringComparison.Ordinal) ? static s => s.City : static s => s.Name;
        return string.Equals(Dir, UiTable.Descending, StringComparison.OrdinalIgnoreCase)
            ? Suppliers.OrderByDescending(by, StringComparer.Ordinal)
            : Suppliers.OrderBy(by, StringComparer.Ordinal);
    }

    // The record the address names, copied for the form once per opening.
    private SupplierListRow Drafted()
    {
        if (!string.Equals(_drafted, Edit, StringComparison.Ordinal))
        {
            _drafted = Edit;
            _draft = Array.Find(Suppliers, s => string.Equals(s.Id.ToString(CultureInfo.InvariantCulture), Edit, StringComparison.Ordinal)) is { } found
                ? found with { }
                : new SupplierListRow(0, "", "");
        }

        return _draft;
    }
}

/// <summary>A supplier as the list shows it.</summary>
public sealed record SupplierListRow(int Id, string Name, string City)
{
    public string Name { get; set; } = Name;
}
