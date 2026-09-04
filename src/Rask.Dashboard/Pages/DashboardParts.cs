using System.Globalization;
using Rask.Core.Routing;

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

/// <summary>
/// The console's shared class strings, named once so a panel does not spell a card out again.
/// </summary>
/// <remarks>
/// Strings rather than components where the shape varies: a card is a <c>div</c> with a border, and
/// wrapping every one of those in a type would buy indirection rather than meaning. What DOES get a
/// component is anything with behaviour or a fixed internal structure — see <c>Ui/</c>.
/// </remarks>


/// <summary>The placeholder a panel shows while its first read is in flight.</summary>
internal sealed partial class DashboardLoading : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex items-center gap-2 py-8 text-sm text-ui-muted")[
            // A pure-CSS spinner: one more reason this package ships no assets.
            Span.Class("size-4 animate-spin rounded-full border-2 border-ui-line border-t-ui-muted")
                .Attributes(("aria-hidden", "true")),
            Span["Reading…"]
        ];
}

/// <summary>The empty state a panel shows when a read came back with nothing to display.</summary>
internal sealed partial class DashboardEmpty : Component
{
    // Not `Title`: that name is the <title> tag's builder entry, inherited from Component.
    public required string Heading { get; set; }

    public required string Detail { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class($"{UiStyles.Card} text-center")[
            Div.Class("text-base font-medium text-ui-ink")[Heading],
            Div.Class("mt-1 text-sm text-ui-muted")[Detail]
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

    /// <inheritdoc />
    protected override Component? Render() =>
        Message is null
            ? null
            : Div.Role("alert")
                .Class(
                    "mb-4 flex items-start gap-3 rounded-xl border border-ui-danger/30 bg-ui-danger/5 px-4 py-3 "
                    + "text-sm text-ui-danger")[
                UiIcon.Name(UiIconName.Warning).Class("mt-0.5 size-5 shrink-0"),
                Span.Class("min-w-0 break-words")["Couldn't read: ", Message]
            ];
}

/// <summary>
/// The notice a panel shows while its poll loop is parked, with the button that resumes it. Renders
/// nothing when the loop is running.
/// </summary>
internal sealed partial class DashboardParked : Component
{
    public bool Parked { get; set; }

    public Func<Task>? Resume { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Parked
            ? Div.Class("mt-4 flex flex-wrap items-center gap-3 text-xs text-ui-muted")[
                Span["Live updates paused to keep the database free."],
                Button.Type("button").Class(UiStyles.Button).OnClickAsync(ResumeAsync)["Resume"]
            ]
            : null;

    private Task ResumeAsync() => Resume?.Invoke() ?? Task.CompletedTask;
}
