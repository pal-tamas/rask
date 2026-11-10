namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectCreateSearchExample : Component
{
    private readonly List<(int Id, string Name)> _projects = [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure")];
    private string _search = "";

    private int? ProjectId { get; set; }

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Bind(() => ProjectId).Combobox.Filter(false)[
                Ui.SelectInput.Placeholder("Start typing...").OnInput(Search),
                Matching().Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
                // Greyed and out of reach while a search is on its way, and spinning once pressed until the project is made.
                Ui.SelectOptionCreate.MinLength(2).OnClick(CreateProject)["Create new project"]
            ],
            Ui.Text.Data("testid", "ui-select-create-search-state")[$"Project: {Named(ProjectId)}."]
        ];

    // The rows that match the search, and the one picked even when it does not: the select goes on naming it.
    private IEnumerable<(int Id, string Name)> Matching() =>
        _projects.Where(project => project.Id == ProjectId || project.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)).Take(20);

    private string Named(int? id) => _projects.FirstOrDefault(project => project.Id == id).Name ?? "none";

    // A slow store, as a database over the network is: long enough to see the create row wait for the answer.
    private async Task Search(string text)
    {
        await Task.Delay(600.Milliseconds);
        _search = text.Trim();
    }

    private async Task CreateProject(string name)
    {
        await Task.Delay(600.Milliseconds);
        var id = _projects.Max(project => project.Id) + 1;
        _projects.Add((id, name.Trim()));
        ProjectId = id;
    }
}
