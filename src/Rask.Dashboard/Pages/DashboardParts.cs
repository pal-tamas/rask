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

    private static string Humanize(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => $"{span.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}s",
        { TotalMinutes: < 60 } => $"{span.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)}m",
        { TotalHours: < 24 } => $"{span.TotalHours.ToString("0.#", CultureInfo.InvariantCulture)}h",
        _ => $"{span.TotalDays.ToString("0.#", CultureInfo.InvariantCulture)}d",
    };
}

/// <summary>The placeholder a panel shows while its first read is in flight.</summary>
internal sealed partial class DashboardLoading : Component
{
    protected override Component? Render() =>
        Div(Class: "d-flex align-items-center gap-2 text-body-secondary py-4")[
            BsSpinner(Small: true),
            Span()["Reading…"]
        ];
}

/// <summary>The empty state a panel shows when a read came back with nothing to display.</summary>
internal sealed partial class DashboardEmpty : Component
{
    // Not `Title`: that name is the <title> tag's builder entry, inherited from Component.
    public required string Heading { get; set; }

    public required string Detail { get; set; }

    protected override Component? Render() =>
        BsCard(Class: "text-center py-5")[
            BsCardBody()[
                Div(Class: "fs-5 fw-semibold")[Heading],
                Div(Class: "text-body-secondary mt-1")[Detail]
            ]
        ];
}

/// <summary>
/// Shown when a read threw. A dashboard that silently stops updating is worse than one that says it
/// couldn't read, so the panel keeps its last values and puts the reason on top. Renders nothing when
/// there is no message, so a panel can hand it the read error unconditionally.
/// </summary>
internal sealed partial class DashboardError : Component
{
    public string? Message { get; set; }

    protected override Component? Render() =>
        Message is null
            ? null
            : BsAlert(Color: BsColor.Danger, Class: "d-flex align-items-center gap-2")[
                BsIcon(Name: BsIconName.ExclamationTriangle),
                Span()["Couldn't read: ", Message]
            ];
}

/// <summary>
/// The notice a panel shows while its poll loop is parked, with the button that resumes it. Renders
/// nothing when the loop is running.
/// </summary>
internal sealed partial class DashboardParked : Component
{
    public bool Parked { get; set; }

    public HandlerAsync? Resume { get; set; }

    protected override Component? Render() =>
        Parked
            ? Div(Class: "d-flex align-items-center gap-2 text-body-secondary small mt-3")[
                Span()["Live updates paused to keep the database free."],
                BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, OnClickAsync: ResumeAsync)[
                    "Resume"]
            ]
            : null;

    private Task ResumeAsync() => Resume?.InvokeAsync() ?? Task.CompletedTask;
}
