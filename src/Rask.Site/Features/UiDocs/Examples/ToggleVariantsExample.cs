namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleVariantsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Toggle.Icon(Ui.IconName.Bolt),
            Ui.Toggle.Icon(Ui.IconName.Bolt).Filled,
            Ui.Toggle.Icon(Ui.IconName.Bolt).Ghost,
            Ui.Toggle.Icon(Ui.IconName.Bolt).Subtle
        ];
}
