using System.Globalization;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IPerformance" /> — high-resolution clock and page-load (navigation) timing.</summary>
public sealed class PerformanceDemo(IPerformance performance) : Component
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private string? _value;
    private string? _status;

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Button(Class: "btn btn-outline-primary btn-sm mb-2", Id: "perf-read", OnClickAsync: Read)[
                    "Read performance timing"],
                Div(Class: "small text-secondary")["Timing: ", Code(Id: "perf-value")[_value ?? "(not requested)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "perf-status")[_status ?? "(idle)"]]
            ]
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
