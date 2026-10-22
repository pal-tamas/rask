namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectCreateExample : Component
{
    private readonly List<(int Id, string Name)> _projects = [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure")];

    private int? ProjectId { get; set; }

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Bind(() => ProjectId).Live().Combobox.Id("ui-select-create")[
                Ui.SelectInput.Placeholder("Start typing..."),
                _projects.Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
                Ui.SelectOptionCreate.MinLength(2).OnClick(CreateProject)["Create new project"]
            ],
            Ui.Text.Data("testid", "ui-select-create-state")[
                $"Project: {(ProjectId is { } id ? _projects.Find(project => project.Id == id).Name : "none")}."
            ]
        ];

    // The page's answer to the create row: add the project, and pick it.
    private void CreateProject(string name)
    {
        var id = _projects.Max(project => project.Id) + 1;
        _projects.Add((id, name));
        ProjectId = id;
    }
}
