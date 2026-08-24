using Android.App;
using Android.OS;
using Android.Webkit;
using Rask.Native;

namespace Rask.Example.Native.WasmSpike;

// SPIKE (#775, epic #774) — the Android half of "does a published Rask WASM client boot inside a platform
// WebView, served from the app's own bundled-asset origin?" The iOS half passed; this answers the same
// question against Chromium and the `https://appassets.rask/` origin.
//
// Mirrors Platforms/iOS/AppDelegate.cs: the WebView is wired here rather than via RaskAndroidWebView so the
// spike can log every request and every console line, but resolution still goes through the SHIPPING
// NativeOriginAssets.Resolve + AndroidBundledAssets.Read. What is under test is the real asset path; only
// the plumbing around it is local.
[Activity(Label = "Rask WASM Spike", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        WebView.SetWebContentsDebuggingEnabled(true);

        var web = new WebView(this);
        var settings = web.Settings;
        settings.JavaScriptEnabled = true;
        // The Mono runtime caches its boot config in localStorage, and the app's own state may use it too;
        // it is off by default in a bare WebView and its absence surfaces as an opaque boot failure.
        settings.DomStorageEnabled = true;

        web.SetWebViewClient(new LoggingWebViewClient(RaskAndroidWebView.DefaultOrigin, AndroidBundledAssets.Read));
        web.SetWebChromeClient(new ConsoleSink());

        SetContentView(web);
        web.LoadUrl(RaskAndroidWebView.DefaultOrigin);
    }
}

// Every request the page makes, with the content type the SHIPPING table chose for it. That table is the
// thing under test: a .wasm served as application/octet-stream is what costs WebAssembly streaming
// instantiation, and it degrades to a console warning rather than a native error.
internal sealed class LoggingWebViewClient(string origin, Func<string, byte[]?> readStaticFile) : WebViewClient
{
    // The page boots long after OnPageFinished (the runtime downloads and starts asynchronously), so the
    // stylesheet probe is deferred rather than run immediately. It reports what actually reached the CSSOM,
    // which is the difference between "the bytes were served" and "the styles applied".
    public override void OnPageFinished(WebView? view, string? url)
    {
        base.OnPageFinished(view, url);
        view?.PostDelayed(() => view.EvaluateJavascript(StyleProbe, null), 8000);
    }

    private const string StyleProbe = """
        (function () {
            var links = Array.prototype.map.call(document.querySelectorAll('link[rel=stylesheet]'), function (l) {
                var rules = -1;
                try { rules = l.sheet ? l.sheet.cssRules.length : -1; } catch (e) { rules = -2; }
                return l.getAttribute('href') + ' sheet=' + (l.sheet ? 'yes' : 'NO') + ' rules=' + rules;
            });
            console.log('[probe] styleSheets=' + document.styleSheets.length + ' links=' + links.length);
            links.forEach(function (l) { console.log('[probe] ' + l); });
            var b = document.body;
            console.log('[probe] body.bg=' + getComputedStyle(b).backgroundColor +
                        ' font=' + getComputedStyle(b).fontFamily);
            Array.prototype.forEach.call(document.querySelectorAll('link[rel=stylesheet]'), function (l) {
                console.log('[probe] node parent=' + l.parentNode.nodeName + ' html=' + l.outerHTML);
            });
            fetch('/global.css').then(function (r) {
                console.log('[probe] fetch /global.css status=' + r.status + ' type=' + r.headers.get('content-type'));
                return r.text();
            }).then(function (t) {
                console.log('[probe] fetch /global.css len=' + t.length + ' head=' + JSON.stringify(t.slice(0, 80)));
            }).catch(function (e) { console.log('[probe] fetch failed ' + e); });
        })();
        """;

    public override WebResourceResponse? ShouldInterceptRequest(WebView? view, IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        if (url is null || !url.StartsWith(origin, StringComparison.Ordinal))
        {
            return base.ShouldInterceptRequest(view, request);
        }

        var path = new Uri(url).AbsolutePath;

        // FINDING (#780), the same on both platforms: NativeOriginAssets.Resolve claims "/", "/index.html"
        // and "/index.native.html" for the NATIVE boot shell before it ever consults the bundle, so the
        // published WASM shell at the origin root is unreachable THROUGH IT. This interceptor takes those
        // paths back for the app's own index.html — which is the mode the resolver itself needs, and the
        // shape of the fix #780 owes.
        //
        // Serving it anywhere else is not merely cosmetic. The iOS spike used /app.html and the router
        // seeded /app.html as the initial route and rendered "Page not found"; at the root the same bundle
        // reports `initial path=/` and renders the real home page. The document's path is load-bearing.
        (byte[] Body, string ContentType)? resolved;
        if (path is "/" or "/index.html" or "/app.html")
        {
            resolved = readStaticFile("index.html") is { } shell ? (shell, "text/html") : null;
        }
        else
        {
            resolved = NativeOriginAssets.Resolve(path, readStaticFile);
        }

        if (resolved is not { } asset)
        {
            Console.WriteLine($"[spike/miss] {path}");
            return new WebResourceResponse("text/plain", "UTF-8", 404, "Not Found",
                new Dictionary<string, string>(), new MemoryStream([]));
        }

        Console.WriteLine($"[spike/serve] {path} -> {asset.ContentType} ({asset.Body.Length} bytes)");

        // "UTF-8" is what the SHIPPING ShowcaseWebViewClient passes, so that is what is under test here.
        return new WebResourceResponse(asset.ContentType, "UTF-8", 200, "OK",
            new Dictionary<string, string>(), new MemoryStream(asset.Body));
    }
}

internal sealed class ConsoleSink : WebChromeClient
{
    public override bool OnConsoleMessage(ConsoleMessage? message)
    {
        if (message is not null)
        {
            Console.WriteLine(
                $"[spike/page] {message.InvokeMessageLevel()}: {message.Message()} @ {message.SourceId()}:{message.LineNumber()}");
        }

        return true;
    }
}
