namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Toggle.Icon(Ui.IconName.Bolt),
            Ui.Toggle.Icon(Ui.IconName.Bolt).Sm,
            Ui.Toggle.Icon(Ui.IconName.Bolt).Xs
        ];
}
