namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxCreateExample : Component
{
    private readonly List<(int Id, string Name)> _tags = [(1, "Design"), (2, "Development"), (3, "Marketing"), (4, "Sales")];
    private List<int> _selectedTags = [];
    private string _search = "";

    protected override Component? Render() =>
        Div.Class("max-w-80 space-y-3")[
            Ui.Pillbox.Values(_selectedTags).OnChange(picked => { _selectedTags = [.. picked]; }).Combobox.Id("ui-pillbox-create")[
                Ui.PillboxInput.Value(_search).OnInput(text => { _search = text; }).Placeholder("Choose tags..."),
                _tags.Select(tag => Ui.PillboxOption.Key(tag.Id).Value(tag.Id)[tag.Name]),
                Ui.PillboxOptionCreate.MinLength(2).OnClick(CreateTag)[$"Create new \"{_search}\""]
            ],
            Ui.Text.Data("testid", "ui-pillbox-create-state")[$"Tags: {Named(_selectedTags)}."]
        ];

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
