namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectSelectedSuffixExample : Component
{
    private List<string> Industries { get; set; } = ["Photography", "Accounting"];

    protected override Component? Render() =>
        Ui.Select.Bind(() => Industries).Listbox.SelectedSuffix("industries selected").Multiple().Class("max-w-sm")[
            Ui.SelectOption["Photography"],
            Ui.SelectOption["Design services"],
            Ui.SelectOption["Web development"],
            Ui.SelectOption["Accounting"],
            Ui.SelectOption["Legal services"],
            Ui.SelectOption["Consulting"],
            Ui.SelectOption["Other"]
        ];
}
