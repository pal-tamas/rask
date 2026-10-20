namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class IconExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap gap-4")[
            Ui.Icon.Name(Ui.IconName.Bolt),
            Ui.Icon.Name(Ui.IconName.Bell),
            Ui.Icon.Name(Ui.IconName.Calendar),
            Ui.Icon.Name(Ui.IconName.ChartBar),
            Ui.Icon.Name(Ui.IconName.Cog6Tooth),
            Ui.Icon.Name(Ui.IconName.Envelope)
        ];
}
