namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectCreateSearchExample : Component
{
    private readonly List<(int Id, string Name)> _projects = [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure")];
    private string _search = "";

    private int? ProjectId { get; set; }

    protected override Component? Render() =>
        Ui.Select.Bind(() => ProjectId).Combobox.Filter(false).Class("max-w-sm")[
            Ui.SelectInput.Placeholder("Start typing...").OnInput(text => { _search = text.Trim(); }),
            Matching().Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
            Ui.SelectOptionCreate.MinLength(2).OnClick(CreateProject)["Create new project"]
        ];

    // The rows that match the search, and the one picked even when it does not: the select goes on naming it.
    private IEnumerable<(int Id, string Name)> Matching() =>
        _projects.Where(project => project.Id == ProjectId || project.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase));

    private void CreateProject(string name)
    {
        var id = _projects.Max(project => project.Id) + 1;
        _projects.Add((id, name.Trim()));
        ProjectId = id;
    }
}
