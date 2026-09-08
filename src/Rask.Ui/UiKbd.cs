namespace Rask.Ui;

/// <summary>
/// A key on a keyboard.
/// </summary>
public sealed partial class UiKbd : Component
{
    public new required string Text { get; set; }

    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Kbd.Class(UiClass.Compose(
            "kbd",
            Size is { } size ? UiClassNames.KbdSize(size) : "",
            Class))[Text];
}
