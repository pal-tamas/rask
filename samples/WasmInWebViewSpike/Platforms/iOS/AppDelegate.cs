using System.Text;
using Foundation;
using Rask.Native;
using UIKit;
using WebKit;

namespace Rask.Example.Native.WasmSpike;

// SPIKE (#775, epic #774) — "does a published Rask WASM client boot inside a WKWebView, served from the
// app's own custom-scheme origin?" That is the one unknown behind the `wasm` hosting model (#780).
//
// The WebView is configured here rather than via RaskWkWebView for one reason: WKWebViewConfiguration is
// COPIED when the WKWebView is constructed, so a user script added afterwards never runs — the first cut of
// this spike lost every console message that way. Resolution still goes through the SHIPPING
// NativeOriginAssets.Resolve + IosBundledAssets.Read, so what is under test is the real asset path; only
// the plumbing around it is local.
[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds);

        var config = new WKWebViewConfiguration();
        config.SetUrlSchemeHandler(new LoggingSchemeHandler(), RaskWkWebView.DefaultScheme);

        var controller = new WKUserContentController();
        controller.AddUserScript(new WKUserScript(
            new NSString(ConsoleBridge), WKUserScriptInjectionTime.AtDocumentStart, isForMainFrameOnly: true));
        controller.AddScriptMessageHandler(new ConsoleSink(), "spike");
        config.UserContentController = controller;

        var web = new WKWebView(UIScreen.MainScreen.Bounds, config)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight
        };
        if (OperatingSystem.IsIOSVersionAtLeast(16, 4))
        {
            web.Inspectable = true;
        }

        Window.RootViewController = new UIViewController { View = web };
        Window.MakeKeyAndVisible();

        web.LoadRequest(new NSUrlRequest(new NSUrl(RaskWkWebView.DefaultOrigin + "app.html")));
        return true;
    }

    private const string ConsoleBridge = """
        (function () {
            function send(kind, args) {
                try {
                    var parts = [];
                    for (var i = 0; i < args.length; i++) { parts.push(String(args[i])); }
                    window.webkit.messageHandlers.spike.postMessage(kind + ": " + parts.join(" "));
                } catch (e) { }
            }
            ["log", "warn", "error"].forEach(function (k) {
                var original = console[k].bind(console);
                console[k] = function () { send(k, arguments); original.apply(null, arguments); };
            });
            window.addEventListener("error", function (e) {
                send("onerror", [e.message + " @ " + e.filename + ":" + e.lineno]);
            });
            window.addEventListener("unhandledrejection", function (e) {
                send("unhandledrejection", [String(e.reason)]);
            });
        })();
        """;
}

// Every request the page makes, with the content type the SHIPPING table chose for it. That table is the
// thing under test: a .wasm served as application/octet-stream is what breaks WebAssembly streaming
// instantiation, and it fails as a console message rather than a native error.
internal sealed class LoggingSchemeHandler : NSObject, IWKUrlSchemeHandler
{
    public void StartUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask)
    {
        var url = urlSchemeTask.Request.Url;
        var path = url?.Path ?? "/";

        // FINDING (#780): NativeOriginAssets.Resolve claims "/", "/index.html" and "/index.native.html" for
        // the NATIVE boot shell before it ever consults the bundle, so the published WASM shell at the
        // origin root is unreachable through it. The WASM shell must live at the root because its
        // <base href="/"> resolves main.js and _framework/* from there — so the resolver needs a mode where
        // the app's own index.html wins. Served here under /app.html to get past it: <base href="/"> means
        // the document's own path does not matter, only what its relative URLs resolve to.
        (byte[] Body, string ContentType)? resolved;
        if (path == "/app.html")
        {
            resolved = IosBundledAssets.Read("index.html") is { } shell ? (shell, "text/html") : null;
        }
        else
        {
            resolved = NativeOriginAssets.Resolve(path, IosBundledAssets.Read);
        }

        if (resolved is not { } asset)
        {
            Console.WriteLine($"[spike/miss] {path}");
            urlSchemeTask.DidReceiveResponse(new NSHttpUrlResponse(url!, 404, "HTTP/1.1", new NSDictionary()));
            urlSchemeTask.DidFinish();
            return;
        }

        Console.WriteLine($"[spike/serve] {path} -> {asset.ContentType} ({asset.Body.Length} bytes)");

        var headers = NSDictionary.FromObjectsAndKeys(
            [new NSString(asset.ContentType), new NSString(asset.Body.Length.ToString())],
            [new NSString("Content-Type"), new NSString("Content-Length")]);
        urlSchemeTask.DidReceiveResponse(new NSHttpUrlResponse(url!, 200, "HTTP/1.1", headers));
        urlSchemeTask.DidReceiveData(NSData.FromArray(asset.Body));
        urlSchemeTask.DidFinish();
    }

    public void StopUrlSchemeTask(WKWebView webView, IWKUrlSchemeTask urlSchemeTask) { }
}

internal sealed class ConsoleSink : NSObject, IWKScriptMessageHandler
{
    public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message) =>
        Console.WriteLine("[spike/page] " + message.Body);
}
