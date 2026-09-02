using Microsoft.JSInterop;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Component with only a sibling <c>JsOnlyDemo.js</c> — no CSS. Regression case:
///     pre-cutover, JS-only components silently dropped out of head emission because the
///     mounted-set was populated from a CSS-presence gate. Now they emit a
///     <c>&lt;script src="/_rask/a/{hash}.js" defer&gt;</c> tag like any other.
/// </summary>
public sealed partial class JsOnlyDemo(IJSRuntime js) : Component
{
    private string _clicks = "0";

    protected override Component? Render() =>
        Div.Class("flex gap-3 items-center flex-wrap items-center")[
            Button.Class($"{Tw.BtnOutlinePrimary} js-only-btn").Type("button").OnClickAsync(HandleClickAsync)[
                "Click to bump (via scoped JS)"],
            Span.Class("text-slate-500 dark:text-slate-400")["Bumped ", Strong[_clicks], " times"]
        ];

    private async Task HandleClickAsync()
    {
        var next = await js.InvokeAsync<int>("Rask.JsOnlyDemo.bump");
        _clicks = next.ToString();
    }
}
