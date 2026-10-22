namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectClearableExample : Component
{
    private string? Industry { get; set; } = "Photography";

    protected override Component? Render() =>
        Ui.Select.Bind(() => Industry).Listbox.Clearable().Placeholder("Choose industry...").Class("max-w-sm")[
            Ui.SelectOption["Photography"],
            Ui.SelectOption["Design services"],
            Ui.SelectOption["Web development"],
            Ui.SelectOption["Accounting"],
            Ui.SelectOption["Legal services"],
            Ui.SelectOption["Consulting"],
            Ui.SelectOption["Other"]
        ];
}
