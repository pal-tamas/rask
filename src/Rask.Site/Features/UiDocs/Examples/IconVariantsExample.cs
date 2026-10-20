namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class IconVariantsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Icon.Name(Ui.IconName.Bolt),
            Ui.Icon.Name(Ui.IconName.Bolt).Solid,
            Ui.Icon.Name(Ui.IconName.Bolt).Mini,
            Ui.Icon.Name(Ui.IconName.Bolt).Micro
        ];
}
