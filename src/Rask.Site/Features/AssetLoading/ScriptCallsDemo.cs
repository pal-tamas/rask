namespace Rask.Site.Features;

/// <summary>
///     Calls its own scoped TypeScript like private methods: <c>WindowSize()</c>, <c>HalfSize()</c>,
///     <c>NewCountdown(5)</c> and the countdown's <c>Start</c>/<c>Stop</c> are generated from <c>ScriptCallsDemo.ts</c>.
/// </summary>
public sealed partial class ScriptCallsDemo : Component
{
    private Countdown? _countdown;
    private string _status = "Idle";

    protected override Component? Render() =>
        Div[
            Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                Ui.Button.Tone(Ui.Tone.Primary).OnClick(StartCountdown)["Count down from 5"],
                Ui.Button.Variant(Ui.Variant.Outline).OnClick(StopCountdown)["Stop"],
                Ui.Button.Variant(Ui.Variant.Outline).OnClick(ReadViewport)["Read the window size"],
                Ui.Button.Variant(Ui.Variant.Outline).OnClick(ReadHalf)["Half the window"]
            ],
            P.Class("text-sm text-ui-muted mb-0 script-calls-status")[_status]
        ];

    private async Task StartCountdown()
    {
        _countdown ??= await NewCountdown(5);
        await _countdown.Start(left => _status = $"{left} left", () => _status = "Done!");
        _status = "5 left";
    }

    private async Task StopCountdown()
    {
        if (_countdown is not null)
        {
            await _countdown.Stop();
            _status = "Stopped";
        }
    }

    private async Task ReadHalf()
    {
        var (width, height) = await HalfSize();
        _status = $"Half: {width:F0} × {height:F0}";
    }

    private async Task ReadViewport()
    {
        var size = await WindowSize();
        _status = $"Window: {size.Width:F0} × {size.Height:F0}";
    }
}
