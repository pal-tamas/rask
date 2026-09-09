namespace Rask.Ui;

/// <summary>
/// A proportion drawn as a ring.
/// </summary>
/// <remarks>
/// daisyUI draws this from a CSS variable rather than from an attribute, so the percentage travels in an
/// inline <c>style</c>. <c>role="progressbar"</c> and the value attributes are set explicitly: the ring is
/// a div, and nothing about a div says what it is measuring.
/// </remarks>
public sealed partial class UiRadialProgress : Component
{
    /// <summary>The accessible name — what is progressing.</summary>
    public new required string Label { get; set; }

    /// <summary>0 to 100.</summary>
    public required int Percent { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var clamped = Math.Clamp(Percent, 0, 100);
        var text = clamped.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Div
            .Role("progressbar")
            .Class(UiClass.Compose("radial-progress", Class))
            .Style($"--value:{text}")
            .Aria(new Dictionary<string, string?>
            {
                ["label"] = Label,
                ["valuenow"] = text,
                ["valuemin"] = "0",
                ["valuemax"] = "100",
            })[$"{text}%"];
    }
}
