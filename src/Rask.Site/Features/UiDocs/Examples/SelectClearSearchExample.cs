namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectClearSearchExample : Component
{
    private List<string> Industries { get; set; } = [];

    protected override Component? Render() =>
        Ui.Select.Bind(() => Industries).Listbox.Searchable().Multiple().Clear(Ui.SelectClear.Close)
            .Placeholder("Choose industries...").Class("max-w-sm")[
            Ui.SelectOption["Photography"],
            Ui.SelectOption["Design services"],
            Ui.SelectOption["Web development"],
            Ui.SelectOption["Accounting"],
            Ui.SelectOption["Legal services"],
            Ui.SelectOption["Consulting"],
            Ui.SelectOption["Other"]
        ];
}
