namespace Rask.Site.Features;

/// <summary>
///     A report behind <c>Cached</c>: shown once it is drawn and its markup kept; hidden and shown again it is handed
///     the kept markup, and the report is never mounted — its caption still says draw #1. Forget it and the next show draws
///     it afresh. The buttons sit next to the fragment, never in it: what it keeps is static.
/// </summary>
public sealed partial class CachedReportDemo : Component
{
    private readonly ReportDraws _draws = new();
    private bool _shown = true;
    private int _shows;

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Div.Class("flex flex-wrap items-center gap-2")[
                Ui.Button.Id("cached-toggle").OnClick(Toggle)[_shown ? "Hide the report" : "Show the report"],
                Ui.Button.Id("cached-forget").OnClick(Forget)["Forget it"]
            ],
            _shown
                // Keyed by the showing, so each show is a fresh fragment that looks its markup up again.
                ? Cached.Name("demo-report").For(10.Minutes).Key(_shows)[CachedReport.Draws(_draws)]
                : P.Class("text-sm text-zinc-500")["Hidden."]
        ];

    private void Toggle()
    {
        _shown = !_shown;
        _shows++;
    }

    private async Task Forget() => await CachedFragments.Forget("demo-report");
}

/// <summary>How many times the report has actually been drawn.</summary>
public sealed class ReportDraws
{
    /// <summary>The draws so far.</summary>
    public int Count { get; set; }
}
