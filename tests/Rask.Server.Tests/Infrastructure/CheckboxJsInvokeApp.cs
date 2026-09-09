using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Components;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Infrastructure;

// Reproduces the Binding-page checkbox in isolation: a bound bool checkbox + an echo of
// the model value, plus a per-render IJSRuntime invoke (like CodeSample) so clicks ship a
// diff carrying jsInvokes — the exact wire shape the showcase produces. Used to verify the
// checkbox stays in sync across repeated clicks on the diff path.
public sealed partial class CheckboxJsInvokeApp(IJSRuntime js) : Component
{
    private readonly Model _m = new();

    protected override async Task OnRenderedAsync(bool firstRender) =>
        await js.InvokeVoidAsync("test.noop", firstRender);

    protected override Component? HeadAssets => new Title()["checkbox"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Form.Model(_m)[Input.Bind(() => _m.Subscribe).Id("sub")],
        new P()[$"S={_m.Subscribe}"]
    ];

    private sealed class Model
    {
        public bool Subscribe { get; set; }
    }
}
