using System.Globalization;

namespace Rask.Dashboard.Pages;

/// <summary>
/// The small pieces every panel repeats — a loading placeholder, an empty state, the read-failure alert,
/// the parked-poll notice, and the two formatters. Kept in one place so the panels read as their data
/// rather than as markup.
/// </summary>
internal static class DashboardParts
{
    public static Component Loading() =>
        Div(Class: "d-flex align-items-center gap-2 text-body-secondary py-4")[
            BsSpinner(Small: true),
            Span()["Reading…"]
        ];

    public static Component Empty(string title, string detail) =>
        BsCard(Class: "text-center py-5")[
            BsCardBody()[
                Div(Class: "fs-5 fw-semibold")[title],
                Div(Class: "text-body-secondary mt-1")[detail]
            ]
        ];

    /// <summary>
    /// Shown when a read threw. A dashboard that silently stops updating is worse than one that says it
    /// couldn't read, so the panel keeps its last values and puts the reason on top.
    /// </summary>
    public static Component? Error(string? message) =>
        message is null
            ? null
            : BsAlert(Color: BsColor.Danger, Class: "d-flex align-items-center gap-2")[
                BsIcon(Name: BsIconName.ExclamationTriangle),
                Span()["Couldn't read: ", message]
            ];

    public static Component? Parked(bool parked, Func<Task> resume) =>
        parked
            ? Div(Class: "d-flex align-items-center gap-2 text-body-secondary small mt-3")[
                Span()["Live updates paused to keep the database free."],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, OnClickAsync: () => resume())["Resume"]
            ]
            : null;

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

    private static string Humanize(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => $"{span.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}s",
        { TotalMinutes: < 60 } => $"{span.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)}m",
        { TotalHours: < 24 } => $"{span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture)}h",
        _ => $"{span.TotalDays.ToString("0.#", CultureInfo.InvariantCulture)}d",
    };
}
