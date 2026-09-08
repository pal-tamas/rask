namespace Rask.Ui;

/// <summary>
/// A number that animates as it changes.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI animates the digits from a CSS variable, so the value travels in an inline <c>style</c> rather
/// than as text — the text inside is what a reader without CSS sees and what a screen reader announces,
/// so both are rendered.
/// </para>
/// <para>
/// It does not count down on its own. Nothing here runs a timer, because the kit ships no JavaScript;
/// the owning page re-renders it with a new value, and daisyUI animates the transition.
/// </para>
/// </remarks>
public sealed partial class UiCountdown : Component
{
    public required int Value { get; set; }

    /// <summary>The accessible name — what is being counted.</summary>
    public required string Label { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var text = Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Span
            .Class(UiClass.Compose("countdown", Class))
            .Aria(new Dictionary<string, string?> { ["label"] = Label })[
            Span.Style($"--value:{text}").Attributes(("aria-hidden", "true"))[text]
        ];
    }
}
