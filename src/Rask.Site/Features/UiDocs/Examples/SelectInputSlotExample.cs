namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectInputSlotExample : Component
{
    private string _search = "";
    private string? _industry;

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Value(_industry).OnChange(value => { _industry = value; }).Combobox[
                Ui.SelectInput.Value(_search).OnInput(text => { _search = text; }).Placeholder("Choose industry..."),
                Ui.SelectOption["Photography"],
                Ui.SelectOption["Design services"],
                Ui.SelectOption["Web development"],
                Ui.SelectOption["Accounting"],
                Ui.SelectOption["Legal services"],
                Ui.SelectOption["Consulting"],
                Ui.SelectOption["Other"]
            ],
            Ui.Text[$"The page holds the search: \"{_search}\"."]
        ];
}
