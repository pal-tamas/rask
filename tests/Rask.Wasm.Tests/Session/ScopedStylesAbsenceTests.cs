using System.Text.Json;
using Rask.Core;
using Rask.Core.ScopedAssets;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Wasm.Tests.Session;

/// <summary>
///     Verifies WASM no longer ships scoped CSS/JS inline via the live payload —
///     per-component assets reach the browser as <c>&lt;link href="/_rask/a/{hash}.css"&gt;</c>
///     tags emitted into the rendered HTML and served by <c>Rask.Wasm.Hosting</c>'s
///     content-addressed endpoint. Historically WASM shipped a <c>cssText</c> payload field
///     that bypassed <c>&lt;base href&gt;</c> under sub-path hosting and re-shipped on every
///     hash bump; the new model gives the browser stable immutable URLs to cache.
/// </summary>
[Collection("WasmSession")]
public class ScopedStylesAbsenceTests : ResettingTestBase
{
    public ScopedStylesAbsenceTests() =>
        ScopedAssetRegistry.RegisterCss(typeof(ScopedCssStubApp), ".tag { color: red; }");

    [Fact]
    public async Task InitialRender_AppWithScopedCss_EmitsContentAddressedLink_NoInlineCssText()
    {
        var (session, _) = NewSession<ScopedCssStubApp>();

        var payload = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(payload.AsMemory());

        // Negative: legacy inline-text delivery is gone. The payload no longer carries
        // cssText (and even if a writer still includes the field, it must be null/empty).
        if (doc.RootElement.TryGetProperty("cssText", out var cssTextEl))
        {
            Assert.True(cssTextEl.ValueKind == JsonValueKind.Null
                        || string.IsNullOrEmpty(cssTextEl.GetString()),
                "WASM payload must not ship cssText inline — assets are content-addressed");
        }

        // Positive: the rendered HTML contains the single scoped-CSS bundle <link> to the asset
        // endpoint with a 12-hex content hash.
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.Matches(@"<link rel=""stylesheet"" href=""/_rask/a/[0-9a-f]{12}\.css""", html);
        // The bundle <link> carries the stable framework morph key.
        Assert.Contains("data-rask-key=\"rsk-css\"", html);
    }

    private sealed class ScopedCssStubApp : Component
    {
        protected override Component? HeadAssets => Title["wasm-stub"];

        protected override string? HtmlLang => null;

        protected override Component? Render() => Div.Class("tag")["hi"];
    }
}
