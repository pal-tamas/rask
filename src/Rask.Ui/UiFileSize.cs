using System.Globalization;

namespace Rask;

/// <summary>A byte count the way <see cref="UiFileItem" /> shows it: B, KB, MB or GB, in steps of 1024.</summary>
internal static class UiFileSize
{
    private const double Step = 1024;

    private static readonly string[] Units = ["B", "KB", "MB", "GB"];

    /// <summary><c>162400</c> → <c>159 KB</c>, as Flux's page shows it; from a megabyte up, to one decimal.</summary>
    /// <param name="bytes">The size in bytes.</param>
    internal static string Format(long bytes)
    {
        double size = Math.Max(bytes, 0);
        var unit = 0;
        while (size >= Step && unit < Units.Length - 1)
        {
            size /= Step;
            unit++;
        }

        var figure = unit < 2 ? Math.Round(size, MidpointRounding.AwayFromZero) : Math.Round(size, 1, MidpointRounding.AwayFromZero);

        return string.Create(CultureInfo.InvariantCulture, $"{figure:0.#} {Units[unit]}");
    }
}
