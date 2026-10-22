namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxLoadingExample : Component
{
    private static readonly string[] Tags = ["Design", "Development", "Marketing", "Sales", "Support", "Engineering", "Product", "Operations"];

    private List<string> _selectedTags = [];
    private string[] _found = Tags;

    protected override Component? Render() =>
        Ui.Pillbox.Values(_selectedTags).OnChange(picked => { _selectedTags = [.. picked]; }).Combobox.Filter(false).Class("max-w-80")[
            Ui.PillboxInput.OnInput(Search).Debounce(250.Milliseconds).Placeholder("Choose tags..."),
            _found.Select(tag => Ui.PillboxOption.Key(tag)[tag]),
            Ui.PillboxOptionEmpty.WhenLoading("Loading tags...")["No tags found."]
        ];

    // A slow store: the list says "Loading tags..." until it answers.
    private async Task Search(string text)
    {
        await Task.Delay(600);
        _found = [.. Tags.Where(tag => tag.Contains(text.Trim(), StringComparison.CurrentCultureIgnoreCase))];
    }
}
