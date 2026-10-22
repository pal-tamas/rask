namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxValidationExample : Component
{
    private readonly List<(int Id, string Name)> _tags = [(1, "Design"), (2, "Development"), (3, "Marketing"), (4, "Sales")];
    private List<int> _selectedTags = [];
    private string _search = "";
    private string? _error;

    protected override Component? Render() =>
        Ui.Field.Class("max-w-80")[
            Ui.Label["Tags"],
            Ui.Pillbox.Values(_selectedTags).OnChange(picked => { _selectedTags = [.. picked]; }).Combobox.Filter(false).Placeholder("Choose tags...")[
                Ui.PillboxInput.Value(_search).OnInput(Search).Invalid(_error is not null).Placeholder("Choose tags..."),
                _tags.Where(tag => tag.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
                    .Select(tag => Ui.PillboxOption.Key(tag.Id).Value(tag.Id)[tag.Name]),
                Ui.PillboxOptionCreate.MinLength(2).OnClick(CreateTag)[$"Create \"{_search}\""]
            ],
            Ui.Error.Message(_error)
        ];

    // A new search clears the last refusal, as Flux's example resets its error bag.
    private void Search(string text)
    {
        _search = text.Trim();
        _error = null;
    }

    private void CreateTag(string name)
    {
        name = name.Trim();
        if (name.Length > 20)
        {
            _error = "A tag is 20 characters at most.";
            return;
        }

        var id = _tags.Max(tag => tag.Id) + 1;
        _tags.Add((id, name));
        _selectedTags = [.. _selectedTags, id];
        _search = "";
    }
}
