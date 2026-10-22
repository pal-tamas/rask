namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxAllExample : Component
{
    private ICollection<string> People { get; set; } = ["mara"];

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.CheckboxGroup.Bind(() => People).Live().Id("cb-people")[
                Ui.CheckboxAll.Label("Everyone"),
                Ui.Checkbox.Value("mara").Label("Mara Lind"),
                Ui.Checkbox.Value("theo").Label("Theo Brandt"),
                Ui.Checkbox.Value("rosa").Label("Rosa Vidal")
            ],
            Ui.Text.Data("testid", "ui-checkbox-all-state")[
                People.Count == 0 ? "Nobody chosen." : $"Chosen: {string.Join(", ", People)}."
            ]
        ];
}
