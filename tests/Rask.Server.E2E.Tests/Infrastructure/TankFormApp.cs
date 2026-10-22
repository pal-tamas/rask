using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;

#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     The app a whole page a pick was reported from, as its sources write it: a sidebar layout with the toasts
///     and the leave dialog, a page that reads what it edits in <c>OnUpdated</c>, and the editor as its keyed child.
/// </summary>
public sealed partial class TankApp : Component
{
    protected override Component? HeadAssets =>
    [
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Base.Href("/uj/"),
        Ui.AppearanceScript,
        Ui.SidebarScript,
        Markup.Title["PPTA"],
    ];

    protected override Component Shell(Component head, Component body) =>
        Html.Lang("hu")[
            head,
            Body.Class("min-h-screen bg-white dark:bg-zinc-800")[body]];

    protected override Component? Render() => Router;
}

[Authorize]
[Route("/")]
public sealed partial class TankLayout(IUserProvider users, RouteState route) : Component
{
    protected override Component? HeadAssets => Title[route.Title is { } title ? title + " | PPTA" : "PPTA"];

    private const string Ground = "border-zinc-200 bg-zinc-50 dark:border-zinc-700 dark:bg-zinc-900";

    protected override Component? Render() =>
    [
        Ui.Sidebar.Sticky(true).Collapsible(Ui.SidebarCollapsible.Mobile).Class("border-e " + Ground)[
            Ui.SidebarHeader[Ui.SidebarBrand.Name("PPTA").Href(Routes.TankListPage()), Ui.SidebarCollapse],
            Ui.Text.Id("partner").Class("px-2 sidebar-rail:hidden")["Partner"],
            Ui.SidebarNav.Class("min-h-0 flex-1 overflow-y-auto")[
                Ui.SidebarItem.Key("tanks").Icon(Ui.IconName.Home).Href(Routes.TankListPage()).Current(true)["Tartályok"]
            ],
            Ui.Dropdown.Top.Start[
                Ui.SidebarProfile.Name(users.Current.Identity?.Name ?? string.Empty),
                Ui.Menu[Ui.MenuItem.Icon(Ui.IconName.ArrowRightStartOnRectangle).Href(Routes.TankListPage())["Kijelentkezés"]]
            ]
        ],
        Ui.Header.Class("border-b " + Ground)[
            Div.Class("lg:hidden")[Ui.SidebarToggle.Inset(Ui.Position.Left)],
            Ui.Breadcrumbs[
                Ui.BreadcrumbsItem.Key("home").Href(Routes.TankListPage()).Icon(Ui.IconName.Home),
                Ui.BreadcrumbsItem.Key("page")[route.Title ?? string.Empty]
            ],
            Ui.Spacer,
            null
        ],
        Ui.Main[Outlet],
        Ui.Toast,
        Ui.ConfirmLeave.Stay("Nem").Leave("Igen"),
        Footer[P.Id("ready")["ready"]],
    ];
}

[Route("tanks")]
[ParentRoute(typeof(TankLayout))]
public sealed partial class TankListPage : Component
{
    protected override Component? Render() => P.Id("tanks")["Nincs tartály"];
}

/// <summary>The twelve lines the report was narrowed to, under the layout: a keyed multiple listbox alone in a div.</summary>
[Route("why-in")]
[ParentRoute(typeof(TankLayout))]
[Authorize]
public sealed partial class WhyInPage : Component
{
    private List<string> _picked = [];

    protected override Component? Render() =>
        Div.Class("max-w-lg p-6")[
            Ui.Select.Key("list").Values((ICollection<string>)[.. _picked]).Listbox.Multiple().Placeholder("Válasszon…")
                .OnChange((ICollection<string> ids) => { _picked = [.. ids]; })[
                new[] { "Glock", "Beretta", "Walther" }.Select(name => Ui.SelectOption.Key(name).Value(name)[name])
            ]
        ];
}

[Route("tanks/new")]
[ParentRoute(typeof(TankLayout))]
[Authorize(Roles = "admin")]
public sealed partial class NewTankPage : Component
{
    protected override string? PageTitle => "Új tartály";

    protected override Component? Render() => [Ui.Heading.Level(1).Xl.Class("mb-6")[PageTitle], TankEditorShape];
}

[Route("tanks/{Id}")]
[ParentRoute(typeof(TankLayout))]
[Authorize(Roles = "admin")]
public sealed partial class EditTankPage : Component
{
    private string? _saved;
    private bool _read;

    [RouteParam] public string Id { get; set; } = string.Empty;

    protected override string? PageTitle => _saved is null ? null : $"{_saved} módosítása";

    protected override async Task OnUpdated()
    {
        await Task.Yield();
        _saved = "Keleti";
        _read = true;
    }

    protected override Component? Render() => _read
        ? [Ui.Heading.Level(1).Xl.Class("mb-6")[PageTitle], TankEditorShape.Key(Id).TankId(Id).Saved(_saved)]
        : null;
}

/// <summary>The reported editor, its store stubbed: what it loads arrives in <c>OnMount</c>.</summary>
public sealed partial class TankEditorShape : Component
{
    /// <summary>The tank being changed; none for a new one.</summary>
    public string? TankId { get; set; }

    /// <summary>What the page read for that id.</summary>
    public string? Saved { get; set; }

    private const string Required = "Kötelező kitölteni";

    private Tank _form = new();
    private List<(string Id, string Name)> _pistols = [];
    private List<(string Id, string Name)> _companies = [];
    private bool _missing;
    private bool _confirmingRemoval;
    private string _saved = string.Empty;

    protected override async Task OnMount()
    {
        await Task.Yield();
        _pistols = [("7f1c", "Glock"), ("7f2c", "Beretta"), ("7f3c", "Walther")];
        _companies = [("c1", "Mol"), ("c2", "Shell")];
        _form = new Tank { Name = Saved ?? string.Empty };
        _missing = TankId == "none";
    }

    protected override Component? Render() => _missing
        ? Ui.Callout.Danger.Heading("Nincs ilyen tartály.")
        :
        [
            Form.Model(_form).OnSubmit(Save).ConfirmLeave("Biztos elhagyja mentés nélkül az oldalt?").Class("max-w-4xl space-y-6")[
                Div.Class("grid max-w-lg gap-6")[
                    Ui.Field[
                        Ui.Label.Badge("Kötelező")["Megnevezés"],
                        Ui.Input.Bind(() => _form.Name).MaxLength(1000).Autofocus(true).ShowValidation(false).Validate(Named),
                        Ui.Error
                    ],
                    Ui.Field[
                        Ui.Label.Badge("Kötelező")["Méret (L)"],
                        Ui.Input.Bind(() => _form.Size).Type(InputType.Number).ShowValidation(false).Validate(Litres),
                        Ui.Error
                    ],
                    Ui.Field[
                        Ui.Label.Badge("Kötelező")["Pisztolyok"],
                        Ui.Select.Bind(() => _form.PistolIds).Listbox.Multiple().Placeholder("Válasszon…").ShowValidation(false).Validate(AtLeastOne)[
                            _pistols.Select(pistol => Ui.SelectOption.Key(pistol.Id).Value(pistol.Id)[pistol.Name])
                        ],
                        Ui.Error
                    ]
                ],
                Ui.Heading.Level(2).Lg["Virtuális tartályok"],
                Ui.Table[
                    Ui.TableColumns[
                        Ui.TableColumn.Key("name")["Megnevezés"],
                        Ui.TableColumn.Key("size")["Méret (L)"],
                        Ui.TableColumn.Key("critical")["Kritikus szint (L)"],
                        Ui.TableColumn.Key("company")["Cég"],
                        Ui.TableColumn.Key("actions").End[Span.Class("sr-only")["Műveletek"]]
                    ],
                    Ui.TableRows[_form.VirtualTanks.Select(Row)]
                ],
                Ui.Button.Key("add-row").Ghost.Icon(Ui.IconName.Plus).OnClick(() => _form.VirtualTanks.Add(new VirtualTank()))["Új virtuális tartály"],
                Div.Class("flex gap-2")[
                    Ui.Spacer,
                    Ui.Button.Key("cancel").Ghost["Mégse"],
                    Ui.Button.Key("save").Primary.Submit["Mentés"]
                ]
            ],
            P.Id("saved")[_saved],
            Ui.Modal.Open(_confirmingRemoval).OnClose(() => { _confirmingRemoval = false; })[Ui.Heading["Tartály törlése"]]
        ];

    private Component Row(VirtualTank row) =>
        Ui.TableRow.Key(row.Key)[
            Ui.TableCell.Class("align-top")[Ui.Field[
                Ui.Input.Bind(() => row.Name).MaxLength(1000).ShowValidation(false).Validate(Named), Ui.Error]],
            Ui.TableCell.Class("align-top")[Ui.Field[
                Ui.Input.Bind(() => row.Size).Type(InputType.Number).ShowValidation(false).Validate(Litres), Ui.Error]],
            Ui.TableCell.Class("align-top")[Ui.Field[
                Ui.Input.Bind(() => row.CriticalLevel).Type(InputType.Number).ShowValidation(false).Validate(Litres), Ui.Error]],
            Ui.TableCell.Class("align-top")[Ui.Field[
                Ui.Select.Bind(() => row.CompanyId).Placeholder("Válasszon…").ShowValidation(false).Validate(Chosen)[
                    _companies.Select(company => Ui.SelectOption.Key(company.Id).Value(company.Id)[company.Name])
                ], Ui.Error]],
            Ui.TableCell.Key("actions").End.Class("align-top")[
                _form.VirtualTanks.Count > 1
                    ? Ui.Button.Key("remove-row").Ghost.Sm.Square(true).Icon(Ui.IconName.Trash).Tooltip("Virtuális tartály törlése")
                        .OnClick(() => _form.VirtualTanks.Remove(row))
                    : null
            ]
        ];

    private void Save() =>
        _saved = $"{_form.Name}|{string.Join(',', _form.PistolIds)}|{_form.Size}|{string.Join(';', _form.VirtualTanks.Select(row => $"{row.Name},{row.Size},{row.CriticalLevel},{row.CompanyId}"))}";

    private static IEnumerable<string> Named(string value) => string.IsNullOrWhiteSpace(value) ? [Required] : [];

    private static IEnumerable<string> Litres(int? value) => value is >= 1 ? [] : [Required];

    private static IEnumerable<string> AtLeastOne(ICollection<string> pistols) => pistols.Count > 0 ? [] : [Required];

    private static IEnumerable<string> Chosen(string id) => id.Length > 0 ? [] : [Required];

    private sealed class Tank
    {
        public string Name { get; set; } = string.Empty;

        public int? Size { get; set; }

        public List<string> PistolIds { get; set; } = [];

        public List<VirtualTank> VirtualTanks { get; set; } = [new()];
    }

    private sealed class VirtualTank
    {
        public string Key { get; } = Guid.NewGuid().ToString();

        public string Name { get; set; } = string.Empty;

        public int? Size { get; set; }

        public int? CriticalLevel { get; set; }

        public string CompanyId { get; set; } = string.Empty;
    }
}
