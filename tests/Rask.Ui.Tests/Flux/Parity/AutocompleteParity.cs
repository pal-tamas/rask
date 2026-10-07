using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/autocomplete</c>, example by example.</summary>
/// <remarks>
///     One example is rendered on Flux's page. The states are the rendered page's — forty-eight, where the
///     markdown shows three. <c>scripts/flux/parity-autocomplete.mjs</c> opens the list here and on Flux's page
///     and compares what is then on screen.
/// </remarks>
public sealed partial class AutocompleteParity : FluxParity
{
    // The column Flux's docs page sets the example in.
    private const string Column = "display:flex;justify-content:center;max-width:384px;margin:0 auto";

    internal static readonly string[] States =
    [
        "Alabama", "Arkansas", "California", "Colorado", "Connecticut", "Delaware", "Florida", "Georgia", "Hawaii", "Idaho",
        "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan",
        "Minnesota", "Mississippi", "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico",
        "New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island",
        "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington", "West Virginia",
        "Wisconsin", "Wyoming",
    ];

    public override string Page => "autocomplete";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style(Column)[
            Div.Style("width:100%")[
                Ui.Autocomplete.Value("").Label("State of residence")[
                    States.Select(state => (Component)Ui.AutocompleteItem.Key(state)[state])
                ]
            ]
        ]);
    }
}
