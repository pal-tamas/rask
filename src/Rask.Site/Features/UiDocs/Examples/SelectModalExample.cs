namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectModalExample : Component
{
    private readonly List<(int Id, string Name)> _projects = [(1, "Branding"), (2, "Analytics"), (3, "Infrastructure")];
    private bool _creating;

    private int? ProjectId { get; set; }

    private string ProjectName { get; set; } = "";

    protected override Component? Render() =>
        Div.Class("max-w-sm")[
            Ui.Select.Bind(() => ProjectId).Listbox.Placeholder("Choose project...")[
                _projects.Select(project => Ui.SelectOption.Key(project.Id).Value(project.Id)[project.Name]),
                Ui.SelectOptionCreate.OnClick(_ => { _creating = true; })["Create new"]
            ],
            Ui.Modal.Open(_creating).OnClose(() => { _creating = false; }).Class("md:w-96")[
                Div.Class("space-y-6")[
                    Div[
                        Ui.Heading.Level(2).Lg["Create new project"],
                        Ui.Text.Class("mt-2")["Enter the name of the new project."]
                    ],
                    Ui.Input.Bind(() => ProjectName).Label("Name").Placeholder("e.g. 'UX Research'"),
                    Div.Class("flex")[
                        Ui.Spacer,
                        Ui.Button.Primary.OnClick(CreateProject)["Create"]
                    ]
                ]
            ]
        ];

    private void CreateProject()
    {
        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            return;
        }

        var id = _projects.Max(project => project.Id) + 1;
        _projects.Add((id, ProjectName.Trim()));
        ProjectId = id;
        ProjectName = "";
        _creating = false;
    }
}
