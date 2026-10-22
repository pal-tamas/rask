namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class PillboxModalExample : Component
{
    private readonly List<(int Id, string Name)> _tags = [(1, "Design"), (2, "Development"), (3, "Marketing"), (4, "Sales")];
    private List<int> _selectedTags = [];
    private bool _creating;

    private string NewTagName { get; set; } = "";

    protected override Component? Render() =>
        Div.Class("max-w-80")[
            Ui.Pillbox.Values(_selectedTags).OnChange(picked => { _selectedTags = [.. picked]; }).Placeholder("Choose tags...").Id("ui-pillbox-modal")[
                Ui.PillboxOptionCreate.OnClick(_ => { _creating = true; })["Create new"],
                _tags.Select(tag => Ui.PillboxOption.Key(tag.Id).Value(tag.Id)[tag.Name])
            ],
            Ui.Modal.Open(_creating).OnClose(() => { _creating = false; }).Class("md:w-96")[
                Div.Class("space-y-6")[
                    Div[
                        Ui.Heading.Level(2).Lg["Create new tag"],
                        Ui.Text.Class("mt-2")["Enter the name of the new tag."]
                    ],
                    Ui.Input.Bind(() => NewTagName).Label("Name").Placeholder("e.g. 'Research'"),
                    Div.Class("flex")[
                        Ui.Spacer,
                        Ui.Button.Primary.OnClick(CreateTag)["Create"]
                    ]
                ]
            ]
        ];

    private void CreateTag()
    {
        if (string.IsNullOrWhiteSpace(NewTagName))
        {
            return;
        }

        var id = _tags.Max(tag => tag.Id) + 1;
        _tags.Add((id, NewTagName.Trim()));
        _selectedTags = [.. _selectedTags, id];
        NewTagName = "";
        _creating = false;
    }
}
