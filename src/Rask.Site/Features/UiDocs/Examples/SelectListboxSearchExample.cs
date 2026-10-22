namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectListboxSearchExample : Component
{
    // What a store would hold: more rows than a list shows, so the page answers a search with the first few.
    private static readonly string[] Cities =
    [
        "Amsterdam", "Athens", "Berlin", "Bern", "Brussels", "Budapest", "Copenhagen", "Dublin", "Helsinki", "Lisbon",
        "Ljubljana", "Madrid", "Oslo", "Paris", "Prague", "Riga", "Rome", "Stockholm", "Vienna", "Warsaw",
    ];

    private string[] _cities = [.. Cities.Take(6)];
    private int _queries;
    private string? _city;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_city).OnChange(value => { _city = value; }).Listbox.Searchable().Filter(false).Placeholder("Choose a city...").Id("ui-select-store")[
                Ui.SelectSearch.Placeholder("Search cities...").OnInput(Search).Debounce(250.Milliseconds),
                _cities.Select(name => Ui.SelectOption.Key(name)[name])
            ],
            Ui.Text.Data("testid", "ui-select-store-state")[$"City: {_city ?? "none"}. Queries: {_queries}."]
        ];

    // The page's answer to the search: a query, here over a list.
    private void Search(string text)
    {
        _queries++;
        _cities = [.. Cities.Where(name => name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).Take(6)];
    }
}
