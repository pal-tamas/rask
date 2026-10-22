namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectListboxExample : Component
{
    private string? _industry;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_industry).OnChange(value => { _industry = value; }).Listbox.Placeholder("Choose industry...").Id("ui-select-listbox")[
                Ui.SelectOption["Photography"],
                Ui.SelectOption["Design services"],
                Ui.SelectOption["Web development"],
                Ui.SelectOption["Accounting"],
                Ui.SelectOption["Legal services"],
                Ui.SelectOption["Consulting"],
                Ui.SelectOption["Other"]
            ],
            Ui.Text.Data("testid", "ui-listbox-state")[_industry is null ? "Nothing chosen." : $"Chosen: {_industry}."]
        ];
}
