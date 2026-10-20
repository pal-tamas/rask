namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class IconInComponentsExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-4")[
            Ui.Button.Icon(Ui.IconName.ArrowDownTray)["Export"],
            Ui.Badge.Icon(Ui.IconName.Bolt)["Fast"],
            Ui.Input.Of<string>().Icon(Ui.IconName.MagnifyingGlass).Placeholder("Search...")
        ];
}
