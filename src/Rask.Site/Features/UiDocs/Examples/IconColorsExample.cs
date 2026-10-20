namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class IconColorsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-4")[
            Ui.Icon.Name(Ui.IconName.Bolt).Class("text-orange-500"),
            Ui.Icon.Name(Ui.IconName.Bolt).Class("text-emerald-500"),
            Ui.Icon.Name(Ui.IconName.Bolt).Class("text-sky-500"),
            Ui.Icon.Name(Ui.IconName.Bolt).Class("text-zinc-400 dark:text-zinc-500")
        ];
}
