using Rask.Core;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>The pickers: a month paged, a list walked and a colour dragged are sent when a value is chosen, not before.</summary>
public sealed partial class KitPickerFramePage : KitFramePage
{
    private DateOnly _day = new(2026, 1, 15);
    private UiDateRange _stay;
    private DateOnly _arrival = new(2026, 1, 15);
    private DateOnly _typed = new(2026, 1, 15);
    private TimeOnly? _time = new TimeOnly(9, 30);
    private TimeOnly? _typedTime = new TimeOnly(9, 30);
    private string _color = "#ef4444";

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("calendar", Ui.Calendar.Value(_day).Key("day").OnChange(d => _day = d)),
            Part("calendar-range", Ui.Calendar.Range.Value(_stay).Key("stay").OnChange(r => _stay = r)),
            Part("date-picker", Ui.DatePicker.Value(_arrival).Key("arrival").Id("arrival").OnChange(d => _arrival = d)),
            Part("date-picker-input", Ui.DatePicker.Value(_typed).Key("typed").Id("typed").Type(Ui.DatePickerType.Input).OnChange(d => _typed = d)),
            Part("time-picker", Ui.TimePicker.Value(_time).Key("time").Id("time").OnChange(t => _time = t)),
            Part("time-picker-input", Ui.TimePicker.Value(_typedTime).Key("typed-time").Id("typed-time").Type(Ui.TimePickerType.Input).OnChange(t => _typedTime = t)),
            Part("color-picker", Ui.ColorPicker.Value(_color).Id("color").OnChange(c => _color = c))
        ];
}

/// <summary>The select family, counted here and owned elsewhere.</summary>
public sealed partial class KitSelectFramePage : KitFramePage
{
    private string? _fruit = "b";
    private string? _city;
    private string _state = string.Empty;
    private List<string> _tags = ["Alpha"];

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("select-native", Ui.Select.Value(_fruit).Id("native").OnChange(v => _fruit = v)[Options()]),
            Part("select", Ui.Select.Value(_fruit).Listbox.Id("listbox").Placeholder("Choose…").OnChange(v => _fruit = v)[Options()]),
            Part("select-searchable", Ui.Select.Value(_city).Listbox.Searchable().Id("searchable").Placeholder("Choose…").OnChange(v => _city = v)[Options()]),
            Part("select-combobox", Ui.Select.Value(_city).Combobox.Id("combobox").Placeholder("Choose…").OnChange(v => _city = v)[Options()]),
            Part("autocomplete", Ui.Autocomplete.Value(_state).Id("autocomplete").OnChange(v => _state = v)[
                Ui.AutocompleteItem.Key("al")["Alabama"], Ui.AutocompleteItem.Key("ak")["Alaska"], Ui.AutocompleteItem.Key("az")["Arizona"]
            ]),
            Part("pillbox", Ui.Pillbox.Values(_tags).Id("pillbox").Placeholder("Choose…").OnChange(v => _tags = [.. v])[
                Ui.PillboxOption["Alpha"], Ui.PillboxOption["Beta"], Ui.PillboxOption["Gamma"]
            ])
        ];

    private static IEnumerable<Component> Options() =>
    [
        Ui.SelectOption.Key("a").Value("a")["Alpha"],
        Ui.SelectOption.Key("b").Value("b")["Beta"],
        Ui.SelectOption.Key("c").Value("c")["Gamma"],
        Ui.SelectOption.Key("d").Value("d")["Delta"],
    ];
}

/// <summary>A chart: the pointer's row, cursor and tooltip are drawn in the browser.</summary>
public sealed partial class KitChartFramePage : KitFramePage
{
    private static readonly Visit[] Visits = [new("Mon", 12), new("Tue", 18), new("Wed", 9), new("Thu", 22), new("Fri", 17)];

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("chart", Ui.Chart.Value(Visits).Class("aspect-3/1")[
                Ui.ChartSvg[
                    Ui.ChartLine.Field((Visit v) => v.Visitors),
                    Ui.ChartAxis.X.Field((Visit v) => v.Day)[Ui.ChartAxisLine, Ui.ChartAxisTick],
                    Ui.ChartAxis.Y[Ui.ChartAxisGrid, Ui.ChartAxisTick],
                    Ui.ChartCursor
                ],
                Ui.ChartTooltip[
                    Ui.ChartTooltipHeading.Field((Visit v) => v.Day),
                    Ui.ChartTooltipValue.Field((Visit v) => v.Visitors).Label("Visitors")
                ]
            ])
        ];

    private sealed record Visit(string Day, int Visitors);
}

/// <summary>The editor: typing and its toolbar are the engine's, and only the value is sent.</summary>
public sealed partial class KitEditorFramePage : KitFramePage
{
    private string _notes = "<p>Hello</p>";

    protected override Component? Render() =>
        Div.Id("stage")[Part("editor", Ui.Editor.Value(_notes).OnChange(v => _notes = v))];
}

/// <summary>What the kit has beyond Flux's catalogue.</summary>
public sealed partial class KitOwnFramePage : KitFramePage
{
    private static readonly Node[] Files =
    [
        new("src", "src", [new("src/a", "a.cs", []), new("src/b", "b.cs", [])]),
        new("docs", "docs", [new("docs/a", "a.md", [])]),
        new("readme", "README.md", []),
    ];

    private static readonly Package[] Catalog = [new(1, "Rask", 1200), new(2, "Rask.Ui", 800), new(3, "Rask.Data", 300)];

    private int _stars = 3;
    private string? _tag;
    private bool _muted;

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("tree", Ui.Tree.Roots(Files).NodeKey(f => f.Id).Item(f => Span[f.Name]).Label("Files").ExpandDepth(1)
                .NodeText(f => f.Name).Selection(Ui.TreeSelection.One)[f => f.Children]),
            Part("data-grid", Ui.DataGrid.Data(Catalog).RowKey(r => r.Id).Label("Packages")[c => [
                c.Field(r => r.Name).Title("Package").Sortable(),
                c.Field(r => r.Downloads).Title("Downloads").Sortable(),
            ]]),
            Part("rating", Ui.Rating.Value(_stars).Group("score").Label("Rate this").Max(5).OnChange(v => _stars = v)),
            Part("filter", Ui.Filter.Value(_tag).Group("tags").Options([("bug", "bug"), ("docs", "docs")]).ResetLabel("All").OnChange(v => _tag = v)),
            Part("toggle-heard", Ui.Toggle.Icon(Ui.IconName.SpeakerWave).Label("Mute").Checked(_muted).OnChange(v => _muted = v)),
            Part("carousel", Ui.Carousel[Div.Class("carousel-item")["One"], Div.Class("carousel-item")["Two"]]),
            Part("hover-gallery", Ui.HoverGallery[Div["One"], Div["Two"]]),
            Part("fab", Ui.Fab.AccessibleLabel("Compose").Icon(Ui.IconName.Plus)[Ui.Button.Sm.Key("photo")["Photo"]]),
            // Below the fold, so nothing loads it but the reader coming to it.
            Part("lazy", Div.Style("height:2400px"), Lazy.Placeholder(Ui.Skeleton.Id("lazy-placeholder"))[Ui.Card[Ui.Heading["Sales"]]])
        ];

    private sealed record Node(string Id, string Name, Node[] Children);

    private sealed record Package(int Id, string Name, int Downloads);
}

/// <summary>A form that asks before its unsaved edits are left, and the dialog it asks in.</summary>
public sealed partial class KitLeaveFramePage : KitFramePage
{
    private readonly Draft _draft = new();

    protected override Component? Render() =>
        Div.Id("stage")[
            Part("confirm-leave",
                Form.Model(_draft).OnSubmit(_ => { }).NoValidate(true).ConfirmLeave("Leave without saving?")[
                    Input.Bind(() => _draft.Title).Id("leave-title"),
                    Button.Type(ButtonType.Submit)["Save"]
                ],
                NavLink.Href("/elsewhere").Id("leave-go")["Elsewhere"],
                Ui.ConfirmLeave.Stay("Stay").Leave("Leave"))
        ];

    private sealed class Draft
    {
        public string Title { get; set; } = string.Empty;
    }
}
