using System.Globalization;
using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs.Examples;

// The editor over its list, at the list's own address: ?edit=new or ?edit=<id>. A list page reads the key as its own
// [QueryParam("edit")] string? Edit; this example shares its page's address, so it reads it there.
public sealed partial class EditorDialogExample(RouteState route) : Component
{
    private readonly List<Supplier> _suppliers =
    [
        new(1, "Aminoff Freight", "Oslo"), new(2, "Bothman & Sons", "Leeds"), new(3, "Geidt Logistics", "Porto"), new(4, "Levin Oil", "Tartu"),
    ];

    private Supplier _draft = new(0, "", "");
    private string? _drafted;

    private string? Search => Read("q");

    private string? Edit => Read("edit");

    private bool IsNew => string.Equals(Edit, UiEditorDialog.New, StringComparison.Ordinal);

    protected override Component? Render() =>
        Div.Class("grid gap-3")[
            Div.Class("flex flex-wrap items-center gap-2")[
                Ui.Input.Value(Search ?? "").Key("search").Class("w-56").Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search suppliers")
                    .OnChange((string text) => Go.With("q", string.IsNullOrWhiteSpace(text) ? null : text.Trim())),
                Ui.Spacer,
                Ui.Button.Key("new").Primary.Sm.Icon(Ui.IconName.Plus).Href(route.With("edit", UiEditorDialog.New))["New supplier"]
            ],
            Ui.Table.Id("ued-suppliers").ContainerClass("min-w-0")[
                Ui.TableColumns[
                    Ui.TableColumn["Supplier"],
                    Ui.TableColumn.Width(Ui.ColumnWidth.Reference)["City"],
                    Ui.TableColumn.RowMenu
                ],
                Ui.TableRows[
                    _suppliers.Where(Found).Select(supplier => Ui.TableRow.Key(supplier.Id)[
                        Ui.TableCell.Variant(Ui.TableCellVariant.Strong).Cut(supplier.Name),
                        Ui.TableCell.Cut(supplier.City),
                        Ui.RowActions.Key(supplier.Id).Edit(route.With("edit", supplier.Id.ToString(CultureInfo.InvariantCulture)))
                    ])
                ]
            ],
            Ui.EditorDialog.In("edit").Heading(IsNew ? "New supplier" : "Edit supplier")[
                Form.Model(Drafted()).OnSubmit(Save).Id("ued-form").Class("space-y-6")[
                    Ui.Input.Bind(() => _draft.Name).Label("Name"),
                    Ui.Input.Bind(() => _draft.City).Label("City"),
                    Ui.FormButtons.Back(route.With("edit", null)).Save(IsNew ? "Create" : null)
                ]
            ]
        ];

    private bool Found(Supplier supplier) => Search is null || supplier.Name.Contains(Search, StringComparison.OrdinalIgnoreCase);

    // The record the address names, copied for the form once per opening: an app's editor reads it from its database.
    private Supplier Drafted()
    {
        if (!string.Equals(_drafted, Edit, StringComparison.Ordinal))
        {
            _drafted = Edit;
            _draft = _suppliers.Find(supplier => string.Equals(supplier.Id.ToString(CultureInfo.InvariantCulture), Edit, StringComparison.Ordinal)) is { } found
                ? found with { }
                : new Supplier(0, "", "");
        }

        return _draft;
    }

    // Saved, the dialog's key goes and the list, rendering again, shows the record as it is now.
    private void Save(Supplier draft)
    {
        var at = _suppliers.FindIndex(supplier => supplier.Id == draft.Id);
        if (at < 0)
        {
            _suppliers.Add(draft with { Id = _suppliers.Max(supplier => supplier.Id) + 1 });
        }
        else
        {
            _suppliers[at] = draft;
        }

        _drafted = null;
        Go.Without("edit");
    }

    private string? Read(string key) => route.Query.TryGetValue(key, out var value) && value.Count > 0 ? value.ToString() : null;

    public sealed record Supplier(int Id, string Name, string City)
    {
        public string Name { get; set; } = Name;

        public string City { get; set; } = City;
    }
}
