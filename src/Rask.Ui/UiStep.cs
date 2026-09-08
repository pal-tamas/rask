namespace Rask.Ui;

/// <summary>
/// One step in a <see cref="UiSteps" />.
/// </summary>
/// <remarks>
/// <see cref="Tone" /> is what marks a step as reached: daisyUI colours a step only when it carries one,
/// so the steps up to and including the current one take a tone and the rest take none.
/// </remarks>
public sealed partial class UiStep : Component
{
    public new required string Text { get; set; }

    public UiTone? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Li.Class(UiClass.Compose(
            "step",
            Tone is { } tone ? UiClassNames.StepTone(tone) : "",
            Class))[Text];
}
