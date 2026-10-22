namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxBackendSearchExample : Component
{
    private readonly List<(int Id, string Name)> _tags =
        [(1, "Design"), (2, "Development"), (3, "Marketing"), (4, "Sales"), (5, "Support"), (6, "Engineering"), (7, "Product"), (8, "Operations")];

    private List<int> _selectedTags = [];
    private string _search = "";
    private int _searches;

    protected override Component? Render() =>
        Div.Class("max-w-80 space-y-3")[
            Ui.Pillbox.Values(_selectedTags).OnChange(picked => { _selectedTags = [.. picked]; }).Combobox.Filter(false).Id("ui-pillbox-backend")[
                Ui.PillboxInput.OnInput(Search).Debounce(250.Milliseconds).Placeholder("Choose tags..."),
                Matching().Select(tag => Ui.PillboxOption.Key(tag.Id).Value(tag.Id)[tag.Name]),
                Ui.PillboxOptionCreate.MinLength(2).OnClick(CreateTag)[$"Create \"{_search}\""]
            ],
            Ui.Text.Data("testid", "ui-pillbox-backend-state")[$"Tags: {Named(_selectedTags)}. Searches: {_searches}."]
        ];

    // The query: the tags that match, and with no search the picked ones too, so a tag just made is among them.
    private IEnumerable<(int Id, string Name)> Matching() =>
        _tags.Where(tag => tag.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)
                           || (_search.Length == 0 && _selectedTags.Contains(tag.Id))).Take(20);

    private void Search(string text)
    {
        _search = text.Trim();
        _searches++;
    }

    private void CreateTag(string name)
    {
        var id = _tags.Max(tag => tag.Id) + 1;
        _tags.Add((id, name.Trim()));
        _selectedTags = [.. _selectedTags, id];
        _search = "";
    }

    private string Named(List<int> ids) =>
        ids.Count == 0 ? "none" : string.Join(", ", ids.Select(id => _tags.Find(tag => tag.Id == id).Name));
}
