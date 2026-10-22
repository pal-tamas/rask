namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectBackendSearchExample : Component
{
    // What a store would hold. The page answers each search with the rows that match.
    private static readonly (int Id, string Name)[] Projects =
        [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure"), (4, "Documentation"), (5, "Security"), (6, "Performance")];

    private string _search = "";
    private int _searches;
    private int? _projectId;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_projectId).OnChange(value => { _projectId = value; }).Combobox.Filter(false).Id("ui-select-backend")[
                Ui.SelectInput.Placeholder("Start typing...").OnInput(Search).Debounce(250.Milliseconds),
                Projects
                    .Where(project => project.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
                    .Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name])
            ],
            Ui.Text.Data("testid", "ui-select-backend-state")[$"Project: {NameOf(_projectId)}. Searches: {_searches}."]
        ];

    private void Search(string text)
    {
        _searches++;
        _search = text;
    }

    private static string NameOf(int? id) => id is { } found ? Array.Find(Projects, project => project.Id == found).Name : "none";
}
