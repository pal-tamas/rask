using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Components;
using Rask.Html.Components;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Reproduces the Server-example "typing forces a full re-render" bug at its framework
// root: a component that invokes IJSRuntime on EVERY render (like the showcase's
// CodeSample re-highlighting hook) plus a bound input whose state mutation changes
// visible HTML. Before the diff gate stopped treating pending jsInvokes as a full-HTML
// trigger, each keystroke shipped the whole document; now it ships a kind:"diff" frame
// that carries the queued invoke alongside the edit ops.
public sealed partial class JsInvokeBindingApp(IJSRuntime js) : Component
{
    private string _typed = "";

    // Fire on every render (NOT gated on firstRender) to exercise the relaxed gate:
    // the queued invoke must ride the diff payload instead of forcing full HTML.
    protected override async Task OnRenderedAsync(bool firstRender) =>
        await js.InvokeVoidAsync("test.noop", firstRender);

    protected override Component? HeadAssets => new Title()["js-invoke-binding"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Input.Value(_typed).OnInput(v => _typed = v),
        new P()[$"Echo: {_typed}"]
    ];
}
