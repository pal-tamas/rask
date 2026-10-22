namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class AutocompleteExample : Component
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

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Autocomplete.Value(_state).OnChange(text => { _state = text; }).Label("State of residence").Id("ui-autocomplete-state")[
                States.Select(state => Ui.AutocompleteItem.Key(state)[state])
            ],
            Ui.Text.Data("testid", "ui-autocomplete-value")[_state.Length == 0 ? "Nothing typed." : $"State: {_state}."]
        ];
}
