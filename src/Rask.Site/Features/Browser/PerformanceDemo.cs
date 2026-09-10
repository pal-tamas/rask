using System.Globalization;
using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="IPerformance" /> — high-resolution clock and page-load (navigation) timing.</summary>
public sealed partial class PerformanceDemo(IPerformance performance) : Component
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                UiButton.Label("Read performance timing").Tone(UiTone.Primary).Variant(UiVariant.Outline).Class("mb-2").Id("perf-read").OnClick(Read),
                Div.Class("text-sm text-ui-muted")["Timing: ", Code.Id("perf-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("perf-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            var now = await performance.NowAsync();
            var t = await performance.GetNavigationTimingAsync();
            _value = t is null
                ? string.Create(Inv, $"now {now:F0} ms (no navigation entry)")
                : string.Create(Inv,
                    $"TTFB {t.TimeToFirstByteMs:F0} ms, DOMContentLoaded {t.DomContentLoadedMs:F0} ms, load {t.LoadMs:F0} ms");
            _status = "Performance read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }
}
