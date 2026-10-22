namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CheckboxFieldsetExample : Component
{
    private bool _english = true;
    private bool _spanish = true;
    private bool _french;
    private bool _german;

    protected override Component? Render() =>
        Ui.Fieldset[
            Ui.Legend["Languages"],
            Ui.Description["Choose the languages you want to support."],
            Div.Class("flex flex-wrap gap-4 *:gap-x-2")[
                Ui.Checkbox.Checked(_english).OnChange(on => { _english = on; }).Label("English"),
                Ui.Checkbox.Checked(_spanish).OnChange(on => { _spanish = on; }).Label("Spanish"),
                Ui.Checkbox.Checked(_french).OnChange(on => { _french = on; }).Label("French"),
                Ui.Checkbox.Checked(_german).OnChange(on => { _german = on; }).Label("German")
            ]
        ];
}
