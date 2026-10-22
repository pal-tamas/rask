namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectComboboxExample : Component
{
    private string? _industry;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_industry).OnChange(value => { _industry = value; }).Combobox.Placeholder("Choose industry...").Id("ui-select-combobox")[
                Ui.SelectOption["Photography"],
                Ui.SelectOption["Design services"],
                Ui.SelectOption["Web development"],
                Ui.SelectOption["Accounting"],
                Ui.SelectOption["Legal services"],
                Ui.SelectOption["Consulting"],
                Ui.SelectOption["Other"]
            ],
            Ui.Text.Data("testid", "ui-combobox-state")[$"Industry: {_industry ?? "none"}."]
        ];
}
