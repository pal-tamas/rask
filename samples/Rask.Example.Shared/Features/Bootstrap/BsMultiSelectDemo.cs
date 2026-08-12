namespace Rask.Example.Shared.Features;

// Every BsMultiSelect<TItem> variant: a dropdown of checkable options with the picks shown as removable
// chips, bound to a model collection. Filter adds an in-dropdown search field; Floating wraps the label like
// a form-floating; Disabled makes it read-only. A live readout OUTSIDE the Form echoes each selection with no
// StateHasChanged (a bound write re-renders the expression's owner).
public sealed partial class BsMultiSelectDemo : Component
{
    private static readonly string[] Interests = ["Web", "Mobile", "AI", "Games", "DevOps", "Data"];

    private readonly Model _m = new();

    // Groups the interests under .dropdown-header sections (first-seen order: Frontend, Data, Other).
    private static string Category(string i) => i switch
    {
        "Web" or "Mobile" => "Frontend",
        "AI" or "Data" => "Data",
        _ => "Other",
    };

    protected override Component? Render() =>
    [
        Form.Model(_m).Class("vstack gap-3")[
            // 1. Basic — chips + checkable dropdown, bound to a List<string>.
            BsMultiSelect.Bind(() => _m.Basic)
                .Options(Interests)
                .Label("Interests (basic)")
                .Placeholder("Pick a few…")
                .Id("ms-basic"),
            // 2. Searchable — a Filter predicate adds a search field that narrows the options.
            BsMultiSelect.Bind(() => _m.Searchable)
                .Options(Interests)
                .Filter((i, t) => i.Contains(t, StringComparison.OrdinalIgnoreCase))
                .Label("Interests (searchable)")
                .Placeholder("Search…")
                .Id("ms-search"),
            // 3. Floating — the label floats up once anything is picked (or while focused).
            BsMultiSelect.Bind(() => _m.Floating)
                .Options(Interests)
                .Label("Interests (floating)")
                .Floating(true)
                .Id("ms-float"),
            // 4. Disabled — non-interactive, still shows its bound chips.
            BsMultiSelect.Bind(() => _m.Locked)
                .Options(Interests)
                .Label("Interests (disabled)")
                .Disabled(true)
                .Id("ms-locked"),
            // 5. Grouped + select-all + a disabled option — OptionGroup renders .dropdown-header sections,
            //    SelectAll adds a bulk "Select all / Clear all" header, OptionDisabled greys "Games" (which the
            //    header and the keyboard both skip).
            BsMultiSelect.Bind(() => _m.Grouped)
                .Options(Interests)
                .OptionGroup(Category)
                .SelectAll(true)
                .OptionDisabled(i => i == "Games")
                .Label("Interests (grouped + select all)")
                .Placeholder("Pick a few…")
                .Id("ms-grouped")
        ],
        BsAlert.Color(BsColor.Secondary).Class("mt-3 mb-0")[
            Span.Id("ms-readout")[
                $"Basic: {Join(_m.Basic)} · Search: {Join(_m.Searchable)} · " +
                $"Floating: {Join(_m.Floating)} · Locked: {Join(_m.Locked)} · Grouped: {Join(_m.Grouped)}"
            ]
        ]
    ];

    private static string Join(ICollection<string> items) => items.Count == 0 ? "—" : string.Join(", ", items);

    private sealed class Model
    {
        public List<string> Basic { get; } = [];
        public List<string> Searchable { get; } = [];
        public List<string> Floating { get; } = [];
        public List<string> Locked { get; } = ["AI", "Data"];
        public List<string> Grouped { get; } = [];
    }
}
