namespace Rask.Site.Features.UiKit;

// Flux UI's autocomplete page: https://fluxui.dev/components/autocomplete
public sealed partial class UiKitDataInputDemo
{
    private static readonly string[] States =
    [
        "Alabama", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida", "Georgia", "Hawaii", "Idaho",
        "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan",
        "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico",
        "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island",
        "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia",
        "Wisconsin", "Wyoming",
    ];

    private string _state = "";
    private string _cityTyped = "";
    private string[] _citiesSuggested = [.. Cities.Take(6)];
    private int _cityTypedQueries;

    // The page's answer to what was typed: a query, here over a list.
    private void SuggestCities(string text)
    {
        _cityTyped = text;
        _cityTypedQueries++;
        _citiesSuggested = [.. Cities.Where(name => name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).Take(6)];
    }

    private Component AutocompleteSection() =>
        Section(
            "Autocomplete",
            "An input that suggests what to type and writes the picked suggestion into itself. What it holds is the "
            + "text, so anything may be typed; to show a name and store an id, use the combobox above. With Debounce "
            + "the page holds what was typed once typing has paused, and answers it with the suggestions.",
            Div.Data(Testid("ui-autocomplete")).Class("grid max-w-sm gap-6")[
                Div[
                    Ui.Autocomplete.Value(_state).OnChange(text => _state = text).Label("State of residence").Id("ui-autocomplete-state")[
                        States.Select(state => Ui.AutocompleteItem.Key(state)[state])
                    ]
                ],
                P.Class("text-sm text-ui-muted").Data(Testid("ui-autocomplete-value"))[
                    _state.Length == 0 ? "Nothing typed." : $"State: {_state}."
                ],
                Div[
                    Ui.Autocomplete.Value(_cityTyped).OnChange(SuggestCities).Debounce(250.Milliseconds).Label("City").Id("ui-autocomplete-city")[
                        _citiesSuggested.Select(city => Ui.AutocompleteItem.Key(city)[city])
                    ]
                ],
                P.Class("text-sm text-ui-muted").Data(Testid("ui-autocomplete-city-state"))[
                    $"City: {(_cityTyped.Length == 0 ? "none" : _cityTyped)}. Searches: {_cityTypedQueries}."
                ]
            ]);
}
