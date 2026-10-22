namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectButtonSlotExample : Component
{
    private string? _industry;

    protected override Component? Render() =>
        Ui.Select.Value(_industry).OnChange(value => { _industry = value; }).Listbox.Class("max-w-sm")[
            Ui.SelectButton.Class("rounded-full!").Placeholder("Choose industry..."),
            Ui.SelectOption["Photography"],
            Ui.SelectOption["Design services"],
            Ui.SelectOption["Web development"],
            Ui.SelectOption["Accounting"],
            Ui.SelectOption["Legal services"],
            Ui.SelectOption["Consulting"],
            Ui.SelectOption["Other"]
        ];
}
