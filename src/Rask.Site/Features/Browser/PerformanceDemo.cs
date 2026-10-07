using System.Globalization;

namespace Rask.Site.Features;

/// <summary>MDN's <c>Performance</c>, from Rask.Web — the high-resolution clock and the page-load (navigation) entry.</summary>
public sealed partial class PerformanceDemo : Component
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Ui.Button.Class("mb-2").Id("perf-read").OnClick(Read)["Read performance timing"],
                Div.Class("text-sm text-ui-muted")["Timing: ", Code.Id("perf-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("perf-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var now = await Performance.Now();
            var navigation = (await Performance.GetEntriesByType("navigation")).FirstOrDefault();
            _value = navigation is null
                ? string.Create(Inv, $"now {now:F0} ms (no navigation entry)")
                : string.Create(Inv, $"now {now:F0} ms, page load took {navigation.Duration:F0} ms");
            _status = "Performance read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
