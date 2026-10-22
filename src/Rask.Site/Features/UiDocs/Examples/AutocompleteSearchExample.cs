namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AutocompleteSearchExample : Component
{
    // What a table would hold: more rows than a list shows, so the page answers a search with the first few.
    private static readonly string[] Cities =
    [
        "Amsterdam", "Athens", "Berlin", "Bern", "Brussels", "Budapest", "Copenhagen", "Dublin", "Helsinki", "Lisbon",
        "Ljubljana", "Madrid", "Oslo", "Paris", "Prague", "Riga", "Rome", "Stockholm", "Vienna", "Warsaw",
    ];

    private string _city = "";
    private string[] _found = [.. Cities.Take(6)];
    private int _searches;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Autocomplete.Value(_city).OnChange(Search).Debounce(250.Milliseconds).Label("City").Id("ui-autocomplete-city")[
                _found.Select(city => Ui.AutocompleteItem.Key(city)[city])
            ],
            Ui.Text.Data("testid", "ui-autocomplete-city-state")[
                $"City: {(_city.Length == 0 ? "none" : _city)}. Searches: {_searches}."
            ]
        ];

    private void Search(string text)
    {
        _city = text;
        _searches++;
        _found = [.. Cities.Where(name => name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).Take(6)];
    }
}
