using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Chrome.Components;
using Rask.Core;
using Rask.Html.Components;
using static Rask.Wasm.Tests.Infrastructure.WasmSessionHarness;

namespace Rask.Wasm.Tests.Session;

/// <summary>
///     A WASM app displayed inside a native shell describes its bars for the platform to draw, rather than
///     rendering them as HTML — and a press on one of those bars runs the app's own callback.
/// </summary>
/// <remarks>
///     <para>
///         A WASM app cannot be told it is in a shell the way a server-rendered one is: there is no request
///         it can read, because it boots inside a document that was already fetched. The shell states it on
///         the window instead, and <c>JSInterop.GetShell()</c> is what reads it.
///     </para>
///     <para>
///         <see cref="JSInterop.ShellForTests" /> is process-wide, and the session reads it once at
///         construction — so these tests must not run beside anything else that builds a session. Hence the
///         collection, and the restore in the finally.
///     </para>
/// </remarks>
[Collection("wasm-shell")]
public class NativeShellChromeTests
{
    private static async Task<(string Html, string? Descriptor)> RenderAsync<TApp>(string shell)
        where TApp : Component, new()
    {
        var previous = JSInterop.ShellForTests;
        JSInterop.ShellForTests = shell;
        JSInterop.ResetLastBeginInvokeJsCall();
        try
        {
            var (session, services) = NewSession<TApp>(configure: s =>
            {
                s.AddSingleton<WasmJSRuntime>();
                s.AddSingleton<IJSRuntime>(sp => sp.GetRequiredService<WasmJSRuntime>());
            });

            services.GetRequiredService<WasmJSRuntime>().AttachHost(session);
            var frame = await session.InitialRenderAsync();

            using var doc = JsonDocument.Parse(frame);
            var html = doc.RootElement.GetProperty("html").GetString() ?? string.Empty;

            // The descriptor does NOT ride the frame here, and that is not an oversight. The push happens
            // after the render walk has closed its context, so the runtime takes its outside-a-render path
            // — which on WASM means an immediate call across the bridge rather than a queued one. The bars
            // are native and live outside the DOM, so nothing about them needs to wait for the frame.
            var call = JSInterop.LastBeginInvokeJsCall;
            return (html, call?.Identifier == "__raskNative.applyChrome" ? call.ArgsJson : null);
        }
        finally
        {
            JSInterop.ShellForTests = previous;
        }
    }

    [Fact]
    public async Task In_a_browser_the_bars_render_as_html()
    {
        var (html, descriptor) = await RenderAsync<WasmBarApp>("");

        // The control. An ordinary tab has no bridge to call, so a descriptor would go nowhere.
        Assert.Contains("rask-header-bar", html, StringComparison.Ordinal);
        Assert.Null(descriptor);
    }

    [Fact]
    public async Task In_a_native_shell_the_bars_are_described_instead_of_drawn()
    {
        var (html, descriptor) = await RenderAsync<WasmBarApp>("native");

        Assert.DoesNotContain("rask-header-bar", html, StringComparison.Ordinal);
        Assert.NotNull(descriptor);
        // The bar's own title, so this pins that the app's chrome was described — not merely that some
        // call was made.
        Assert.Contains("Inbox", descriptor, StringComparison.Ordinal);
    }

    /// <summary>The value is matched case-insensitively — a head is not required to guess our casing.</summary>
    [Fact]
    public async Task The_shell_value_is_case_insensitive()
    {
        var (html, _) = await RenderAsync<WasmBarApp>("Native");

        Assert.DoesNotContain("rask-header-bar", html, StringComparison.Ordinal);
    }
}

/// <summary>Serialises the classes that read the process-wide shell flag.</summary>
[CollectionDefinition("wasm-shell")]
public class WasmShellCollection;

internal sealed partial class WasmBarApp : Component
{
    protected override Component? Render() =>
    [
        AppBar.Title("Inbox"),
        Div["body"],
    ];
}
