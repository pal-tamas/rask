namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class SelectCheckboxIndicatorExample : Component
{
    private List<string> Industries { get; set; } = [];

    protected override Component? Render() =>
        Div.Class("max-w-sm space-y-3")[
            Ui.Select.Bind(() => Industries).Live().Listbox.Checkbox.Multiple().Placeholder("Choose industries...").Id("ui-select-checkbox")[
                Ui.SelectOption["Photography"],
                Ui.SelectOption["Design services"],
                Ui.SelectOption["Web development"],
                Ui.SelectOption["Accounting"],
                Ui.SelectOption["Legal services"],
                Ui.SelectOption["Consulting"],
                Ui.SelectOption["Other"]
            ],
            Ui.Text.Data("testid", "ui-select-checkbox-state")[
                Industries.Count == 0 ? "Nothing chosen." : $"Chosen: {string.Join(", ", Industries)}."
            ]
        ];
}
