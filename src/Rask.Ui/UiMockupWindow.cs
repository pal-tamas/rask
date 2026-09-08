namespace Rask.Ui;

/// <summary>
/// A plain window frame.
/// </summary>
public sealed partial class UiMockupWindow : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-window border border-base-300 bg-base-100", Class))[
            Div.Class("border-t border-base-300")[Children ?? []]
        ];
}
