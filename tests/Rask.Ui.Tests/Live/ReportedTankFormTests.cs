using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Messaging;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     The form an app reported whole-page replies from, as that app writes it: an editor that is a child of its
///     page, a fragment of the form and a confirmation, a multiple listbox over a table of rows whose cells are
///     unkeyed but for the last, and a button in that cell only while there are two rows.
/// </summary>
public partial class ReportedTankFormTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";

    private static Page Draw()
    {
        var services = new ServiceCollection().AddSingleton<IToaster, Toaster>().BuildServiceProvider();

        return Page.Render(
            () => Div[
                Header[Nav[A.Href("/")["Home"]]],
                Main[H1["Új tartály"], TankEditorShape],
                Ui.Toast,
                Ui.ConfirmLeave.Stay("Nem").Leave("Igen")
            ],
            services);
    }

    // The twelve lines the report was narrowed to: a keyed multiple listbox alone in a div, over a collection
    // the page holds or a member it binds, with a placeholder or without one.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task A_keyed_multiple_listbox_alone_goes_from_none_to_two_and_back_with_a_diff_each_pick(bool bound, bool placeholder)
    {
        var picked = new Picks();
        var page = Page.Render(() => Div.Class("max-w-lg p-6")[
            (bound
                ? Ui.Select.Key("list").Bind(() => picked.Ids).Listbox.Multiple().Placeholder(placeholder ? "Válasszon…" : null)
                : Ui.Select.Key("list").Values((ICollection<string>)[.. picked.Ids]).Listbox.Multiple().Placeholder(placeholder ? "Válasszon…" : null)
                    .OnChange((ICollection<string> ids) => { picked.Ids = [.. ids]; }))[
                Enumerable.Range(1, 3).Select(n => Ui.SelectOption.Key("o" + n).Value("o" + n)["option " + n])
            ]
        ]);
        await page.On("[popover][data-ui-options]").Raise("toggle", Opened);

        var said = new List<string>();
        foreach (var option in new[] { "option 1", "option 2", "option 2", "option 1" })
        {
            await page.On($"[data-ui-option]:has-text(\"{option}\")").Click();
            said.Add(string.Join(',', picked.Ids));
        }

        Assert.Equal(["o1", "o1,o2", "o1", string.Empty], said);
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Fact]
    public async Task Every_pick_in_the_reported_form_is_answered_with_a_diff()
    {
        var page = Draw();
        await page.On("[popover][data-ui-options]").Raise("toggle", Opened);

        var said = new List<string>();
        foreach (var pistol in new[] { "Glock", "Beretta", "Beretta", "Glock" })
        {
            await page.On($"[data-ui-option]:has-text(\"{pistol}\")").Click();
            said.Add(page.TextOf("[data-ui-select-button]"));
        }

        Assert.Equal(["Glock", "2 selected", "Glock", "Válasszon…"], said);
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Fact]
    public async Task A_row_added_to_the_reported_form_and_taken_away_again_is_answered_with_a_diff()
    {
        var page = Draw();

        await page.Click("Új virtuális tartály");
        var two = page.FindAll("tbody tr").Count;
        await page.Invoke(page.FindAll("button[data-rask-key=\"remove-row\"]")[0].Attribute("data-rask-on-click")!);
        var one = page.FindAll("tbody tr").Count;

        Assert.Equal((2, 1), (two, one));
        Assert.Empty(DiffGuardAttribute.FullPages());
    }
}

/// <summary>What a lone listbox picks into.</summary>
internal sealed class Picks
{
    public List<string> Ids { get; set; } = [];
}

/// <summary>The reported editor, its store stubbed: what it offers is read in <c>OnMount</c>.</summary>
public sealed partial class TankEditorShape : Component
{
    private const string Required = "Kötelező kitölteni";

    private Tank _form = new();
    private List<(string Id, string Name)> _pistols = [];
    private List<(string Id, string Name)> _companies = [];
    private bool _missing;
    private bool _confirmingRemoval;

    // Read before the first render here: a page under test is drawn once, and nothing waits for a store.
    protected override Task OnMount()
    {
        _pistols = [("7f1c", "Glock"), ("7f2c", "Beretta"), ("7f3c", "Walther")];
        _companies = [("c1", "Mol"), ("c2", "Shell")];
        _form = new Tank();

        return Task.CompletedTask;
    }

    protected override Component? Render() => _missing
        ? Ui.Callout.Danger.Heading("Nincs ilyen tartály.")
        :
        [
            Form.Model(_form).OnSubmit(() => { _missing = false; }).ConfirmLeave("Biztos elhagyja mentés nélkül az oldalt?").Class("max-w-4xl space-y-6")[
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
