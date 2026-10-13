using Rask.Core;
using Rask.Core.Routing;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.Tests.Infrastructure;

/// <summary>
///     Pages whose parts are joined by ids the kit makes up — a tooltip and its trigger, a label and its
///     control, a menu and its button — and which read what they show in an awaited <c>OnUpdated</c>, as the
///     list page a first response without <c>aria-describedby</c> was reported from does.
/// </summary>
public sealed partial class IdLinkApp : Component
{
    protected override Component? HeadAssets => Title["id links"];

    protected override Component? Render() => Router;
}

/// <summary>How a read of <see cref="IdLinkStore" /> comes back.</summary>
public enum IdLinkRead
{
    /// <summary>Already there: the hook never leaves the walk.</summary>
    Synchronous,

    /// <summary>After a <c>Task.Yield()</c>.</summary>
    Yielded,

    /// <summary>After a millisecond.</summary>
    Delayed,

    /// <summary>When a pool thread says so.</summary>
    FromAPoolThread,

    /// <summary>When the test lets it go.</summary>
    Held,
}

/// <summary>What the pages read: rows, then how many there are in all — two reads, one after the other.</summary>
public sealed class IdLinkStore
{
    public const int RowCount = 25;

    private TaskCompletionSource? _held;
    private int _heldReads;

    public IdLinkRead Read { get; set; } = IdLinkRead.Delayed;

    /// <summary>Holds every read from now until <see cref="Release" />.</summary>
    public void Hold()
    {
        Volatile.Write(ref _heldReads, 0);
        _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Read = IdLinkRead.Held;
    }

    /// <summary>Completes once <paramref name="reads" /> reads have started since the last <see cref="Hold" />.</summary>
    public Task ReadsStarted(int reads) =>
        Rask.TestSupport.WaitFor.True(() => Volatile.Read(ref _heldReads) >= reads, "the held reads to start");

    /// <summary>Lets the held reads go; a later read is held again until the next release.</summary>
    public void Release() =>
        Interlocked.Exchange(ref _held, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))?.TrySetResult();

    public async Task<string[]> Rows()
    {
        await Come().ConfigureAwait(false);
        return [.. Enumerable.Range(1, RowCount).Select(static n => "Norma " + n)];
    }

    public async Task<int> Count()
    {
        await Come().ConfigureAwait(false);
        return RowCount;
    }

    private Task Come()
    {
        switch (Read)
        {
            case IdLinkRead.Synchronous:
                return Task.CompletedTask;
            case IdLinkRead.Yielded:
                return Yielded();
            case IdLinkRead.Delayed:
                return Task.Delay(1);
            case IdLinkRead.FromAPoolThread:
                var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                ThreadPool.QueueUserWorkItem(static state => ((TaskCompletionSource)state!).TrySetResult(), source);
                return source.Task;
            default:
                Interlocked.Increment(ref _heldReads);
                return _held!.Task;
        }
    }

    private static async Task Yielded() => await Task.Yield();
}

[Route("/id-links")]
public sealed partial class IdLinkLayout(RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } title ? title + " | id links" : "id links"];

    protected override Component? Render() =>
    [
        Ui.Header[Ui.Breadcrumbs[route.Title is { } t ? Ui.BreadcrumbsItem[t] : null], Ui.Spacer],
        Ui.Main[Outlet],
    ];
}

/// <summary>
///     The reported list: a table whose rows arrive from two awaited reads, each ending in a pencil and a bin
///     named by the tooltip the button's own step wraps it in, an eye inside a tooltip the page wrote, and a
///     button with words of its own, which its tooltip describes.
/// </summary>
[Route("rows")]
[ParentRoute(typeof(IdLinkLayout))]
public sealed partial class IdLinkRowsPage(IdLinkStore store) : Component
{
    /// <summary>The buttons in a row a tooltip NAMES: they show an icon and nothing else.</summary>
    public const int NamedPerRow = 3;

    /// <summary>The buttons in a row a tooltip DESCRIBES: they have words of their own.</summary>
    public const int DescribedPerRow = 1;

    private string[] _rows = [];
    private int _total;
    private bool _read;

    protected override string? PageTitle => "Korrekciós normák";

    protected override async Task OnUpdated()
    {
        var rows = await store.Rows();
        var total = await store.Count();

        (_rows, _total, _read) = (rows, total, true);
    }

    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl.Class("mb-6")[PageTitle],
        Ui.Table.Paginate(_read ? Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 25, Total = _total }).OnPage(static _ => { }) : null)[
            Ui.TableColumns[Ui.TableColumn["Megnevezés"], Ui.TableColumn.Key("actions")],
            Ui.TableRows[
                _rows.Select((name, index) => Ui.TableRow.Key(index)[
                    Ui.TableCell[name],
                    Ui.TableCell.End[Actions(index)]
                ])
            ]
        ],
    ];

    private static Component Actions(int index) =>
        Div.Class("flex justify-end gap-1")[
            Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Pencil).Tooltip("Módosítás").Href(Routes.IdLinkKitPage()),
            Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Trash).Tooltip("Törlés").OnClick(static () => { }),
            Ui.Tooltip.Content("Megnyitás")[Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Eye)],
            Ui.Button.Ghost.Sm.Tooltip("A " + (index + 1) + ". sor másolása")["Másolás"]
        ];
}

/// <summary>
///     One of each kit component that joins two elements by an id it made up, shown once a read is back and
///     beside a few that are there from the first wave on.
/// </summary>
[Route("kit")]
[ParentRoute(typeof(IdLinkLayout))]
public sealed partial class IdLinkKitPage(IdLinkStore store) : Component
{
    private string[] _rows = [];
    private bool _read;
    private string _email = "";
    private string _state = "";
    private string _industry = "";
    private string _plan = "free";
    private string _tab = "profile";

    protected override string? PageTitle => "Kit";

    protected override async Task OnUpdated()
    {
        var rows = await store.Rows();
        await store.Count();

        (_rows, _read) = (rows, true);
    }

    protected override Component? Render() =>
    [
        Ui.Heading.Level(1).Xl[PageTitle],
        Ui.Button.Icon(Ui.IconName.Cog6Tooth).Tooltip("Beállítások"),
        Ui.Input.Of<string>().Key("always").Label("Keresés").Description("A lista szűrése."),
        _read ? Linked() : null,
        Ui.Modal.Key("unnamed").Open(false)[Ui.Heading["Törlés"], Ui.Text["Biztos?"]],
    ];

    private Component Linked() =>
        Div.Key("linked").Class("space-y-6")[
            Ui.Field.Key("email")[
                Ui.Label["Email"],
                Ui.Description["Például you@example.com."],
                Ui.Input.Value(_email).OnChange(v => { _email = v; }),
                Ui.Error.Message("Nem tűnik email címnek.")
            ],
            Ui.Input.Of<string>().Key("username").Label("Felhasználónév").Description("Nyilvános."),
            Ui.Textarea.Of<string>().Key("notes").Label("Megjegyzés"),
            Ui.Checkbox.Key("remember").Checked().Label("Emlékezz rám").Description("Ezen az eszközön."),
            Ui.Switch.Value(true).Key("notify").Label("Értesítések").Description("Emailben."),
            Ui.RadioGroup.Value(_plan).Key("plan").Label("Csomag").OnChange(v => { _plan = v; })[
                Ui.Radio.Value("free").Label("Ingyenes").Description("Egy felhasználó."),
                Ui.Radio.Value("team").Label("Csapat")
            ],
            Ui.Select.Key("native").Value(_industry).OnChange(v => { _industry = v; }).Label("Iparág")[
                _rows.Take(3).Select(name => Ui.SelectOption.Key("native-" + name)[name])
            ],
            Ui.Select.Key("listbox").Value(_industry).OnChange(v => { _industry = v; }).Listbox.Label("Ágazat")[
                _rows.Take(3).Select(name => Ui.SelectOption.Key("listbox-" + name)[name])
            ],
            Ui.Autocomplete.Value(_state).Key("state").OnChange(v => { _state = v; }).Label("Megye")[
                _rows.Take(3).Select(name => Ui.AutocompleteItem.Key(name)[name])
            ],
            Ui.Dropdown.Key("options")[
                Ui.Button["Műveletek"],
                Ui.Menu[Ui.MenuItem.Key("rename")["Átnevezés"], Ui.MenuItem.Key("archive")["Archiválás"]]
            ],
            Ui.ModalTrigger.Key("edit").Name("edit-profile")[Ui.Button["Szerkesztés"]],
            Ui.Modal.Key("m-edit").Name("edit-profile")[Ui.Heading["Profil"], Ui.Input.Of<string>().Key("name").Label("Név")],
            Ui.Accordion.Key("faq").Exclusive()[
                Ui.AccordionItem.Heading("Hová szállítanak?")["Bárhová."],
                Ui.AccordionItem.Heading("Módosítható?")["Igen."]
            ],
            Ui.TabGroup.Key("tabs")[
                Ui.Tabs.Value(_tab).OnChange(tab => _tab = tab)[Ui.Tab.Name("profile")["Profil"], Ui.Tab.Name("account")["Fiók"]],
                Ui.TabPanel.Name("profile")["A neve."],
                Ui.TabPanel.Name("account")["Az email címe."]
            ],
            Div.Key("tips").Class("flex gap-2")[
                Ui.Tooltip.Key("named").Content("Megnyitás")[Ui.Button.Icon(Ui.IconName.Eye)],
                Ui.Tooltip.Key("described").Content("Elmenti a piszkozatot")[Ui.Button["Mentés"]],
                Ui.Tooltip.Key("interactive").Interactive()[
                    Ui.Button.Icon(Ui.IconName.InformationCircle),
                    Ui.TooltipContent["Bővebben a ", A.Href("/id-links/rows")["listán"]]
                ],
                Ui.Tooltip.Key("toggled").Toggleable().Content("Érintésre nyílik")[Ui.Button.Icon(Ui.IconName.QuestionMarkCircle)],
                Ui.Tooltip.Key("link").Content("A lista")[A.Href("/id-links/rows")[Ui.Icon.Name(Ui.IconName.Home)]]
            ]
        ];
}
