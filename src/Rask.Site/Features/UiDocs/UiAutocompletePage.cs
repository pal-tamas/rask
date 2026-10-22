using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's autocomplete as a C# component: each example with its own code, and its reference.</summary>
[Route("autocomplete")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiAutocompletePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Autocomplete.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Autocomplete)
            .Lead("An input that suggests what to type and writes the picked suggestion into itself. What it holds is the "
                  + "text, so anything may be typed; to show a name and store an id, use a select's combobox instead.")
            .Api(UiApi.Autocomplete)[
            UiDocsExample.Key("ui-autocomplete").Heading("Autocomplete").File("AutocompleteExample.cs").Demo(AutocompleteExample).Testid("ui-autocomplete")
                .Blurb("Type \"ne\": the list narrows to the states that hold it, and Enter writes the lit one into the input."),
            UiDocsExample.Key("ui-autocomplete-search").Heading("Suggestions from the server").File("AutocompleteSearchExample.cs").Demo(AutocompleteSearchExample).Testid("ui-autocomplete-search")
                .Blurb("Debounce hands the page what was typed once typing has paused, and the page answers with the items: "
                       + "one query for a word, not one for a letter."),
            UiAutocompleteBehaviour
        ];
}
