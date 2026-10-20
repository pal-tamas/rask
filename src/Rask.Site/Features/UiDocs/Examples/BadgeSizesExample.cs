namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BadgeSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-end gap-2")[
            Ui.Badge.Sm["Small"],
            Ui.Badge["Default"],
            Ui.Badge.Lg["Large"]
        ];
}
