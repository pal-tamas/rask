namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class IconSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Icon.Name(Ui.IconName.Bolt).Class("size-12"),
            Ui.Icon.Name(Ui.IconName.Bolt).Class("size-8"),
            Ui.Icon.Name(Ui.IconName.Bolt).Class("size-6"),
            Ui.Icon.Name(Ui.IconName.Bolt).Class("size-4")
        ];
}
