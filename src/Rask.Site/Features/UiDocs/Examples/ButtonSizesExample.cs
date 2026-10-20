namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ButtonSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-end gap-3")[
            Ui.Button["Base"],
            Ui.Button.Sm["Small"],
            Ui.Button.Xs["Extra small"]
        ];
}
