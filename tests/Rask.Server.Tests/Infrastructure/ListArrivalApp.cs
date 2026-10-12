using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     The app two whole pages a save were reported from, as its sources write it: a sidebar of eighty-four
///     links in twelve expandable groups, a header whose breadcrumb reads <c>route.Title</c>, and a list page
///     that reads its rows and its count in <c>OnUpdated</c> and draws a table and a pager once it has them.
/// </summary>
public sealed partial class ListArrivalApp : Component
{
    protected override Component? HeadAssets =>
    [
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Ui.AppearanceScript,
        Ui.SidebarScript,
        Markup.Title["PPTA"],
    ];

    protected override Component Shell(Component head, Component body) =>
        Html.Lang("hu").Attributes((UiStylesheet.ThemeScopeAttribute, ""))[
            head,
            Body.Class("min-h-screen bg-white dark:bg-zinc-800")[body]];

    protected override Component? Render() => Router;
}

/// <summary>What the pages read from. A read is held until the test lets it go, or takes a millisecond.</summary>
public sealed class ListArrivalStore
{
    private TaskCompletionSource? _held;
    private int _heldReads;

    /// <summary>How many reads have started.</summary>
    public int Reads;

    /// <summary>Holds every read from now until <see cref="Release" />.</summary>
    public void Hold()
    {
        Volatile.Write(ref _heldReads, 0);
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>Completes once a read has started since the last <see cref="Hold" />: the page is waiting on it.</summary>
    public Task ReadStarted() => Rask.TestSupport.WaitFor.True(() => Volatile.Read(ref _heldReads) > 0, "a held read to start");

    /// <summary>Lets the held reads go, and every later one take a millisecond again.</summary>
    public void Release() => Interlocked.Exchange(ref _held, null)?.TrySetResult();

    public async Task<string[]> Rows()
    {
        await Read().ConfigureAwait(false);
        return [.. Enumerable.Range(1, 15).Select(static n => "Viszonylat " + n)];
    }

    public async Task<int> Count()
    {
        await Read().ConfigureAwait(false);
        return 43;
    }

    /// <summary>The two reads a list makes, one after the other: its rows, then how many there are in all.</summary>
    public async Task<(string[] Rows, int Total)> Page()
    {
        var rows = await Rows().ConfigureAwait(false);
        return (rows, await Count().ConfigureAwait(false));
    }

    public Task Save() => Task.Delay(1);

    private Task Read()
    {
        Interlocked.Increment(ref Reads);
        if (_held is not { } held)
        {
            return Task.Delay(1);
        }

        Interlocked.Increment(ref _heldReads);
        return held.Task;
    }
}

[Route("/")]
public sealed partial class ListArrivalLayout(RouteState route) : Component
{
    private const string Ground = "border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900";

    private static readonly Ui.IconName[] GroupIcons =
    [
        Ui.IconName.Star, Ui.IconName.Folder, Ui.IconName.ChartBar, Ui.IconName.Users, Ui.IconName.CreditCard,
        Ui.IconName.Truck, Ui.IconName.Tag, Ui.IconName.Wrench, Ui.IconName.Bell, Ui.IconName.Key,
        Ui.IconName.GlobeAlt, Ui.IconName.ArchiveBox,
    ];

    protected override Component? HeadAssets => Title[route.Title is { } title ? title + " | PPTA" : "PPTA"];

    protected override Component? Render() =>
    [
        Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Mobile).Class("border-e " + Ground)[
            Ui.SidebarHeader[Ui.SidebarBrand.Name("PPTA").Href(Routes.ListArrivalListPage()), Ui.SidebarCollapse],
            Ui.SidebarNav.Class("min-h-0 flex-1 overflow-y-auto")[
                Enumerable.Range(0, GroupIcons.Length).Select(Group).ToArray()
            ]
        ],
        Ui.Header.Class("border-b " + Ground)[
            Div.Class("lg:hidden")[Ui.SidebarToggle.Inset(Ui.Position.Left)],
            Ui.Breadcrumbs[
                Ui.BreadcrumbsItem.Href(Routes.ListArrivalListPage()).Icon(Ui.IconName.Home),
                route.Title is { } t ? Ui.BreadcrumbsItem[t] : null
            ],
            Ui.Spacer
        ],
        Ui.Main[Outlet],
        Ui.Toast,
    ];

    // Seven links a group, as the reporter's menu has: eighty-four in all, the first a link of the app.
    private static Component Group(int index) =>
        Ui.SidebarGroup.Key(index).Expandable(true).Icon(GroupIcons[index]).Heading("Csoport " + (index + 1))[
            Enumerable.Range(1, 7).Select(item => (Component)Link(index, item)).ToArray()
        ];

    private static Component Link(int group, int item) =>
        group == 0 && item == 1
            ? Ui.SidebarItem.Key(item).Href(Routes.ListArrivalListPage())["Viszonylatok"]
            : Ui.SidebarItem.Key(item).Href($"/menu/{group + 1}/{item}")[$"Menüpont {group + 1}.{item}"];
}

/// <summary>
///     The list as the reporter writes it: nothing where its table will be until both reads are back, and the
///     dialog that confirms a removal after it — so the table arrives BEFORE a sibling that stays.
/// </summary>
[Route("destinations")]
[ParentRoute(typeof(ListArrivalLayout))]
public sealed partial class ListArrivalListPage(ListArrivalStore store) : Component
{
    private string[]? _rows;
    private int _total;

    protected override string? PageTitle => "Viszonylatok";

    protected override async Task OnUpdated() => (_rows, _total) = await store.Page();

    protected override Component? Render() =>
    [
        Div.Class("mb-6 flex items-center justify-between")[
            Ui.Heading.Level(1).Xl[PageTitle],
            Ui.Button.Href(Routes.ListArrivalNewPage()).Primary["Új viszonylat"]
        ],
        _rows is null ? null : ListArrivalTable.Names(_rows).Total(_total),
        Ui.Modal.Key("confirm-removal").Open(false)[Ui.Heading["Viszonylat törlése"], Ui.Text["Biztos törölni szeretné?"]],
    ];
}

/// <summary>The same list with nothing after its table: the table arrives at the end of its level.</summary>
[Route("destinations-tail")]
[ParentRoute(typeof(ListArrivalLayout))]
public sealed partial class ListArrivalTailPage(ListArrivalStore store) : Component
{
    private string[]? _rows;
    private int _total;

    protected override string? PageTitle => "Viszonylatok";

    protected override async Task OnUpdated() => (_rows, _total) = await store.Page();

    protected override Component? Render() =>
    [
        Div.Class("mb-6 flex items-center justify-between")[Ui.Heading.Level(1).Xl[PageTitle]],
        _rows is null ? null : ListArrivalTable.Names(_rows).Total(_total),
    ];
}

/// <summary>The same list, saying it is loading where its table will be: one element in place of another.</summary>
[Route("destinations-loading")]
[ParentRoute(typeof(ListArrivalLayout))]
public sealed partial class ListArrivalLoadingPage(ListArrivalStore store) : Component
{
    private string[]? _rows;
    private int _total;

    protected override string? PageTitle => "Viszonylatok";

    protected override async Task OnUpdated() => (_rows, _total) = await store.Page();

    protected override Component? Render() =>
    [
        Div.Class("mb-6 flex items-center justify-between")[Ui.Heading.Level(1).Xl[PageTitle]],
        _rows is null ? Ui.Text.Id("loading")["Betöltés…"] : ListArrivalTable.Names(_rows).Total(_total),
    ];
}

/// <summary>Fifteen rows of four cells, the last a pencil and a bin, and the pager under them.</summary>
public sealed partial class ListArrivalTable : Component
{
    public string[]? Names { get; set; }

    public int? Total { get; set; }

    protected override Component? Render() =>
        Ui.Table.Paginate(Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 15, Total = Total ?? 0 }).OnPage(static _ => { }))[
            Ui.TableColumns[
                Ui.TableColumn["Név"],
                Ui.TableColumn["Ország"],
                Ui.TableColumn["Állapot"],
                Ui.TableColumn.End[""]
            ],
            Ui.TableRows[
                (Names ?? []).Select((name, index) => Ui.TableRow.Key(index)[
                    Ui.TableCell.Variant(Ui.TableCellVariant.Strong)[name],
                    Ui.TableCell["Magyarország"],
                    Ui.TableCell.Class("py-0")[Ui.Badge.Sm.Green["Aktív"]],
                    Ui.TableCell.End[
                        Ui.Button.Sm.Ghost.Icon(Ui.IconName.Pencil).Href(Routes.ListArrivalEditPage(index + 1)),
                        Ui.Button.Sm.Ghost.Icon(Ui.IconName.Trash).OnClick(static () => { })
                    ]
                ])
            ]
        ];
}

/// <summary>A new record's page: its save goes to the list, as the documented save does.</summary>
[Route("destinations/new")]
[ParentRoute(typeof(ListArrivalLayout))]
public sealed partial class ListArrivalNewPage(ListArrivalStore store) : Component
{
    protected override string? PageTitle => "Új viszonylat";

    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl.Class("mb-6")[PageTitle],
        Ui.Button.Id("save").Primary.OnClick(SaveThenGo)["Mentés"],
    ];

    private async Task SaveThenGo()
    {
        await store.Save();
        Routes.ListArrivalListPage().Go();
    }
}

/// <summary>The edit page docs/routing.md teaches: the record read in <c>OnUpdated</c>, "no such" until it is there.</summary>
[Route("destinations/{Id}")]
[ParentRoute(typeof(ListArrivalLayout))]
public sealed partial class ListArrivalEditPage(ListArrivalStore store) : Component
{
    private string? _name;

    [RouteParam] public int Id { get; set; }

    protected override string? PageTitle => _name is { } name ? $"{name} módosítása" : null;

    protected override async Task OnUpdated() => _name = (await store.Rows())[Id - 1];

    protected override Component? Render() =>
        _name is null
            ? Ui.Callout.Id("missing")["Nincs ilyen viszonylat"]
            : [Ui.Heading.Level(1).Xl.Class("mb-6")[PageTitle], Ui.Input.Value(_name)];
}

/// <summary>A page that decides, after a read, that the visitor belongs on the list.</summary>
[Route("moved")]
[ParentRoute(typeof(ListArrivalLayout))]
public sealed partial class ListArrivalMovedPage(ListArrivalStore store) : Component
{
    protected override async Task OnMount()
    {
        await store.Save();
        Routes.ListArrivalListPage().Go();
    }

    protected override Component? Render() => P.Id("moved")["moved"];
}
