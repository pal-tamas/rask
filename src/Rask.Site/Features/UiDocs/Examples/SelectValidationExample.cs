namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectValidationExample : Component
{
    private readonly List<(int Id, string Name)> _projects = [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure")];
    private string _search = "";
    private string? _error;

    private int? ProjectId { get; set; }

    protected override Component? Render() =>
        Ui.Field.Class("max-w-sm")[
            Ui.Label["Project"],
            Ui.Select.Bind(() => ProjectId).Combobox.Filter(false).ShowValidation(false)[
                Ui.SelectInput.Placeholder("Start typing...").Invalid(_error is not null).OnInput(Search),
                _projects
                    .Where(project => project.Id == ProjectId || project.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
                    .Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
                Ui.SelectOptionCreate.MinLength(2).OnClick(CreateProject)["Create new project"]
            ],
            Ui.Error.Message(_error)
        ];

    // A new search clears the last refusal, as Flux's example resets its error bag.
    private void Search(string text)
    {
        _search = text.Trim();
        _error = null;
    }

    private void CreateProject(string name)
    {
        name = name.Trim();
        if (name.Length > 40)
        {
            _error = "A project's name is 40 characters at most.";
            return;
        }

        var id = _projects.Max(project => project.Id) + 1;
        _projects.Add((id, name));
        ProjectId = id;
    }
}
