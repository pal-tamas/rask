namespace Rask;

/// <summary>The colours a <see cref="UiChart{T}" /> hands its series in turn when they name no tone of their own.</summary>
internal static class UiChartPalette
{
    private static readonly Ui.Tone[] Tones =
    [
        Ui.Tone.Primary, Ui.Tone.Secondary, Ui.Tone.Accent, Ui.Tone.Info, Ui.Tone.Success, Ui.Tone.Warning, Ui.Tone.Error,
    ];

    /// <summary>The tone for the series at <paramref name="index" />, starting over once every tone is taken.</summary>
    internal static Ui.Tone For(int index) => Tones[index % Tones.Length];
}
