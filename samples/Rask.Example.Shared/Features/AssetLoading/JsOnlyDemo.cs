using Microsoft.JSInterop;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Component with only a sibling <c>JsOnlyDemo.js</c> — no CSS. Regression case:
///     pre-cutover, JS-only components silently dropped out of head emission because the
///     mounted-set was populated from a CSS-presence gate. Now they emit a
///     <c>&lt;script src="/_rask/a/{hash}.js" defer&gt;</c> tag like any other.
/// </summary>
public sealed class JsOnlyDemo(IJSRuntime js) : Component
{
    private string _clicks = "0";

    protected override RenderResult Render() =>
        Div(Class: "d-flex align-items-center gap-3")[
            Button(Class: "btn btn-outline-primary js-only-btn", OnClickAsync: HandleClickAsync)[
                "Click to bump (via scoped JS)"],
            Span(Class: "text-secondary")["Bumped ", Strong()[_clicks], " times"]
        ];

    private async Task HandleClickAsync()
    {
        var next = await js.InvokeAsync<int>("Rask.JsOnlyDemo.bump");
        _clicks = next.ToString();
    }
}
