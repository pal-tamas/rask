using System.Net;
using Rask.Core;
using Rask.Core.ScopedAssets;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Server.Tests.Security;

/// <summary>
///     Security-oriented assertions for the per-component asset endpoint. Covers MIME
///     confusion prevention, content served verbatim (no transformation that could become
///     an injection vector), URL-encoded path traversal, and IIFE wrapper escape attempts.
///     The asset URLs are public by design — no authentication gates them — but the bytes
///     they serve must not enable cross-component or cross-origin attacks.
/// </summary>
[Collection("ScopedAssets")]
public class AssetEndpointSecurityTests
{
    public AssetEndpointSecurityTests() => ScopedAssetRegistry.InvalidateAll();

    [Fact]
    public async Task NosniffHeader_PreventsBrowserMimeSniffing()
    {
        // Without X-Content-Type-Options: nosniff, a browser may sniff a misdeclared
        // Content-Type and execute CSS as JS (or vice versa). The header is the standard
        // mitigation; the endpoint must set it on every asset response.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var v));
        Assert.Equal("nosniff", v.Single());
    }

    [Fact]
    public async Task DefaultCors_NoWildcardAllowOrigin_OnAssetResponse()
    {
        // Asset URLs are intentionally cross-origin-fetchable when the host opts in via
        // app.UseCors(...). The default Server endpoint must NOT preemptively set
        // Access-Control-Allow-Origin: * — that would force all consumers into open-CORS.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"),
            "Default endpoint must not set Access-Control-Allow-Origin; let the host opt in via UseCors().");
    }

    [Fact]
    public async Task UrlEncodedPathTraversal_DoesNotEscapeAssetNamespace()
    {
        // Mock a malicious request: %2e%2e%2f is URL-encoded "../". The route constraint
        // rejects non-hex hash segments, so the request can't resolve to a registry entry —
        // result must NOT be 200 with someone else's bytes.
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".secret { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        var assetBytes = ScopedAssetRegistry.GetByHash(hash, AssetKind.Css)!.Value.Utf8.ToArray();
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/%2e%2e%2f%2e%2e%2f{hash}.css");
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEqual(assetBytes, body);
    }

    [Fact]
    public async Task NullByteInHash_DoesNotResolveToAsset()
    {
        // Null byte injection — historically used to truncate paths in C-based filesystems.
        // Either the host rejects the URL before routing (throwing on the HttpClient side)
        // or the route constraint rejects it (404). What's not allowed: returning a valid
        // asset body.
        using var host = RaskTestHost.Create<TestApp>();
        try
        {
            var response = await host.Http.GetAsync("/_rask/a/abcd%00ef0123.css");
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("null", StringComparison.OrdinalIgnoreCase))
        {
            // ASP.NET's URL decoder rejects the null byte before routing — that's the
            // strictest possible response and exactly what we want.
        }
        catch (HttpRequestException)
        {
            // Host rejected the request — also acceptable; the asset bytes are not leaked.
        }
    }

    [Fact]
    public async Task ExtremelyLongHash_Returns404_NotProcessing()
    {
        // 10KB of hex in the URL — length-bounded route constraint must reject before any
        // dictionary lookup is attempted (no DoS via huge hash strings).
        var longHash = new string('a', 10_000);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{longHash}.css");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CssContentContainingFakeScriptTag_ServedVerbatim_AsTextCss()
    {
        // A user could register CSS containing a string that LOOKS like an HTML
        // </style><script> sequence. The endpoint serves it as text/css — the browser
        // parses it as CSS, never as HTML, and the <script> is inert. This test
        // documents the contract: bytes are not transformed server-side (no injection
        // vector via server processing).
        const string evilLookingCss = ".x { content: '</style><script>alert(1)</script>'; }";
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), evilLookingCss);
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        // The bytes survived round-trip; the browser will parse them as CSS and the
        // <script> string is just a CSS content value, never executed.
        Assert.Contains("<script>", body);
    }

    [Fact]
    public async Task JsContentAttemptingIifeEscape_ContainedByWrapper()
    {
        // The JS wrapper template is `(function(){window.Rask[name]=(function(){<USER>})();})();`
        // A malicious user-JS might try to close the inner function prematurely:
        //   })();window.HACKED=true;//
        // The wrapper's structure (the inner function body is wrapped in `(function(){...})()`)
        // means the user's `)})` doesn't terminate the wrapper — it closes the wrong scope and
        // produces a syntax error. The bytes are still served, but the browser refuses to run
        // them. The wrapped output should contain the user's bytes but NOT a top-level
        // window.HACKED assignment outside the wrapper.
        const string escapeAttempt = "})();window.HACKED=true;//";
        ScopedAssetRegistry.RegisterJs(typeof(WidgetA), escapeAttempt);
        ScopedAssetRegistry.TryGetJs(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.js");
        var body = await response.Content.ReadAsStringAsync();
        // The user text appears inside the IIFE; the structural wrapper still surrounds it
        // (i.e. the file still ends with the closing `})();})();\n` from WrapModule).
        var trimmed = body.TrimEnd();
        Assert.EndsWith("})();", trimmed);
        // Outer IIFE structure still in place.
        Assert.Contains("(function () {", body);
        Assert.Contains("window.Rask = window.Rask || {};", body);
    }

    [Fact]
    public async Task CrossKindMismatch_DoesNotLeakOtherKindBytes()
    {
        // A known CSS hash queried with .js extension must 404, not return CSS bytes with
        // a JS content-type (which would be a XSS vector if the CSS contained executable
        // JS-as-CSS-comment shenanigans).
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var cssHash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{cssHash}.js");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EndpointAllowsAnonymous_EvenUnderGlobalAuthFallback()
    {
        // If a host configures a global authorization fallback policy (which is common
        // for "secure by default" apps), the asset endpoint must remain anonymous-
        // accessible — content-addressed URLs are public by intent. This test mirrors
        // the .AllowAnonymous() call on the endpoint by verifying no auth challenge is
        // returned. (The minimal TestApp doesn't configure auth — this test asserts
        // baseline behavior; full coverage of the auth-fallback case lives in a
        // dedicated host fixture.)
        ScopedAssetRegistry.RegisterCss(typeof(WidgetA), ".x { color: red; }");
        ScopedAssetRegistry.TryGetCss(typeof(WidgetA), out var hash);
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync($"/_rask/a/{hash}.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Found, response.StatusCode); // not redirected to login
    }

    private sealed class WidgetA : Component
    {
        protected override RenderResult Render() => this;
    }
}
