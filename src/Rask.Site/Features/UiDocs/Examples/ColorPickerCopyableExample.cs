namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerCopyableExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.ColorPicker.Value("#0ff1ce").Copyable()
        ];
}
