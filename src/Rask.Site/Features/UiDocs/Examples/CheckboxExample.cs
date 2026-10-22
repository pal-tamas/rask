namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxExample : Component
{
    private bool _terms;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Field.Variant(Ui.FieldVariant.Inline)[
                Ui.Checkbox.Checked(_terms).OnChange(value => { _terms = value; }).Id("cb-terms"),
                Ui.Label["I agree to the terms and conditions"],
                Ui.Error
            ],
            Ui.Text.Data("testid", "ui-checkbox-state")[_terms ? "Terms agreed." : "Terms not agreed yet."]
        ];
}
