namespace Rask.Ui;

/// <summary>
/// A phone around a picture of a screen.
/// </summary>
public sealed partial class UiMockupPhone : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("mockup-phone", Class))[
            Div.Class("mockup-phone-camera"),
            Div.Class("mockup-phone-display")[Children ?? []]
        ];
}
