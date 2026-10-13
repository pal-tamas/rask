namespace Rask;

/// <summary>
///     The swatches a <see cref="UiColorPicker" /> offers, each read once into the colour it stands for.
/// </summary>
/// <param name="Swatches">The swatches, in the order they are drawn.</param>
internal sealed record UiColorPalette(IReadOnlyList<UiColorPalette.Entry> Swatches)
{
    /// <summary>How many swatches stand in one row: what ArrowUp and ArrowDown move by.</summary>
    internal const int Columns = 8;

    // Flux's default palette: 24 Tailwind colours.
    private static readonly string[] Tailwind =
    [
        "#ef4444", "#f97316", "#f59e0b", "#eab308", "#22c55e", "#14b8a6", "#0ea5e9", "#6366f1",
        "#a855f7", "#ec4899", "#f43f5e", "#fb923c", "#fbbf24", "#a3e635", "#34d399", "#22d3ee",
        "#818cf8", "#c084fc", "#f472b6", "#fda4af", "#78716c", "#64748b", "#000000", "#ffffff",
    ];

    private static readonly UiColorValue?[] TailwindColors = [.. Tailwind.Select(value => UiColorValue.TryParse(value, out var color) ? color : (UiColorValue?)null)];

    /// <summary>Flux's default palette, named as Flux names it, in the reader's language.</summary>
    internal static UiColorPalette Standard()
    {
        string[] names =
        [
            RaskStrings.Get(RaskString.ColorSwatchRed, "Red"),
            RaskStrings.Get(RaskString.ColorSwatchOrange, "Orange"),
            RaskStrings.Get(RaskString.ColorSwatchAmber, "Amber"),
            RaskStrings.Get(RaskString.ColorSwatchYellow, "Yellow"),
            RaskStrings.Get(RaskString.ColorSwatchGreen, "Green"),
            RaskStrings.Get(RaskString.ColorSwatchTeal, "Teal"),
            RaskStrings.Get(RaskString.ColorSwatchSky, "Sky"),
            RaskStrings.Get(RaskString.ColorSwatchIndigo, "Indigo"),
            RaskStrings.Get(RaskString.ColorSwatchPurple, "Purple"),
            RaskStrings.Get(RaskString.ColorSwatchPink, "Pink"),
            RaskStrings.Get(RaskString.ColorSwatchRose, "Rose"),
            RaskStrings.Get(RaskString.ColorSwatchOrangeLight, "Orange light"),
            RaskStrings.Get(RaskString.ColorSwatchAmberLight, "Amber light"),
            RaskStrings.Get(RaskString.ColorSwatchLime, "Lime"),
            RaskStrings.Get(RaskString.ColorSwatchEmerald, "Emerald"),
            RaskStrings.Get(RaskString.ColorSwatchCyan, "Cyan"),
            RaskStrings.Get(RaskString.ColorSwatchIndigoLight, "Indigo light"),
            RaskStrings.Get(RaskString.ColorSwatchVioletLight, "Violet light"),
            RaskStrings.Get(RaskString.ColorSwatchPinkLight, "Pink light"),
            RaskStrings.Get(RaskString.ColorSwatchRoseLight, "Rose light"),
            RaskStrings.Get(RaskString.ColorSwatchStone, "Stone"),
            RaskStrings.Get(RaskString.ColorSwatchSlate, "Slate"),
            RaskStrings.Get(RaskString.ColorSwatchBlack, "Black"),
            RaskStrings.Get(RaskString.ColorSwatchWhite, "White"),
        ];

        return new([.. names.Select((name, index) => new Entry(Tailwind[index], name, TailwindColors[index]))]);
    }

    /// <summary>A palette of <paramref name="swatches" />.</summary>
    internal static UiColorPalette From(IReadOnlyList<UiColorSwatch> swatches) =>
        new([.. swatches.Select(swatch => new Entry(swatch.Value, swatch.Label ?? swatch.Value, UiColorValue.TryParse(swatch.Value, out var color) ? color : null))]);

    /// <summary>The first swatch of the colour written <paramref name="hex" />, whatever its alpha, or <c>null</c>.</summary>
    internal Entry? Picked(string hex) => IndexOf(hex) is var index and >= 0 ? Swatches[index] : null;

    /// <summary>Where the swatch of the colour written <paramref name="hex" /> stands, or -1.</summary>
    internal int IndexOf(string hex)
    {
        for (var i = 0; i < Swatches.Count; i++)
        {
            if (string.Equals(Swatches[i].Color?.Hex, hex, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>One swatch.</summary>
    /// <param name="Value">The colour as the page wrote it.</param>
    /// <param name="Named">Its label, or the value when it has none.</param>
    /// <param name="Color">What the value reads as, or <c>null</c> for text that is no colour.</param>
    internal sealed record Entry(string Value, string Named, UiColorValue? Color);
}
