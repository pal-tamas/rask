using System.Globalization;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The two formatters every panel repeats. Purely string-producing — it builds no markup at all, so it
/// needs nothing from the component surface; the panel states it used to carry are components below.
/// </summary>
internal static class DashboardParts
{
    /// <summary>
    /// A UTC instant as "how long ago", which is what an operator actually reads a queue timestamp for.
    /// Callers put the exact instant in the cell's Title, so hovering gives the precise value.
    /// </summary>
    public static string Ago(DateTime utc, DateTime now)
    {
        var delta = now - utc;
        if (delta < TimeSpan.Zero)
        {
            return "in " + Humanize(delta.Negate());
        }

        return delta < TimeSpan.FromSeconds(5) ? "just now" : Humanize(delta) + " ago";
    }

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    public static string Duration(TimeSpan span) => Humanize(span);

    /// <summary>
    /// The last page there is, counted from zero, for <paramref name="total" /> rows in pages of
    /// <paramref name="pageSize" />. A page past it reads empty while the counts above still say otherwise — the
    /// rows drained or were evicted under an operator on a later page — so the panels step back to this one.
    /// </summary>
    public static int LastPageIndex(int total, int pageSize) => total <= 0 ? 0 : (total - 1) / pageSize;

    private static string Humanize(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => $"{span.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}s",
        { TotalMinutes: < 60 } => $"{span.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)}m",
        { TotalHours: < 24 } => $"{span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture)}h",
        _ => $"{span.TotalDays.ToString("0.#", CultureInfo.InvariantCulture)}d",
    };
}
