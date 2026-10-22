namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectExample : Component
{
    private string? _industry;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_industry).OnChange(value => { _industry = value; }).Placeholder("Choose industry...").Id("ui-select-native")[
                Ui.SelectOption["Photography"],
                Ui.SelectOption["Design services"],
                Ui.SelectOption["Web development"],
                Ui.SelectOption["Accounting"],
                Ui.SelectOption["Legal services"],
                Ui.SelectOption["Consulting"],
                Ui.SelectOption["Other"]
            ],
            Ui.Text.Data("testid", "ui-select-state")[_industry is null ? "Nothing chosen." : $"Chosen: {_industry}."]
        ];
}
