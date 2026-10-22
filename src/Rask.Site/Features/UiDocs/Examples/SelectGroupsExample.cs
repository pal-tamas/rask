namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectGroupsExample : Component
{
    private string? _industry;

    protected override Component? Render() =>
        Ui.Select.Value(_industry).OnChange(value => { _industry = value; }).Placeholder("Choose an industry...").Class("max-w-sm")[
            Ui.SelectGroup.Label("Creative")[
                Ui.SelectOption.Value("photography")["Photography"],
                Ui.SelectOption.Value("design")["Design services"]
            ],
            Ui.SelectGroup.Label("Technology")[
                Ui.SelectOption.Value("web-development")["Web development"],
                Ui.SelectOption.Value("it-consulting")["IT consulting"]
            ],
            Ui.SelectGroup.Label("Services")[
                Ui.SelectOption.Value("accounting")["Accounting"],
                Ui.SelectOption.Value("legal")["Legal services"],
                Ui.SelectOption.Value("consulting")["Consulting"]
            ]
        ];
}
