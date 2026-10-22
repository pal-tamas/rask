namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectMultipleExample : Component
{
    private List<string> Industries { get; set; } = [];

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Bind(() => Industries).Live().Listbox.Multiple().Placeholder("Choose industries...").Id("ui-select-multiple")[
                Ui.SelectOption["Photography"],
                Ui.SelectOption["Design services"],
                Ui.SelectOption["Web development"],
                Ui.SelectOption["Accounting"],
                Ui.SelectOption["Legal services"],
                Ui.SelectOption["Consulting"],
                Ui.SelectOption["Other"]
            ],
            Ui.Text.Data("testid", "ui-multiselect-state")[
                Industries.Count == 0 ? "Nothing chosen." : $"Chosen: {string.Join(", ", Industries)}."
            ]
        ];
}
