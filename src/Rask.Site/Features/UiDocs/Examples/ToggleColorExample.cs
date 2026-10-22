namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleColorExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Toggle.Icon(Ui.IconName.Bolt).Color(Ui.Color.Blue).Checked(),
            Ui.Toggle.Icon(Ui.IconName.Heart).Color(Ui.Color.Red).Checked(),
            Ui.Toggle.Icon(Ui.IconName.Bell).Color(Ui.Color.Amber).Checked()
        ];
}
