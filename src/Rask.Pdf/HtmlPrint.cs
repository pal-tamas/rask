using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Rask.Printing;

/// <summary>
/// Prints one HTML document in a browser context of its own — no cookie, storage or cache shared with any other
/// document — and closes the context after. The page has no network: the document is served from memory under
/// <see cref="DocumentUrl"/>, every other request is refused, and scripts do not run.
/// </summary>
internal static class HtmlPrint
{
    /// <summary>The address the document is served under. <c>.invalid</c> never resolves, so nothing can leave the machine.</summary>
#pragma warning disable S1075 // not a place to configure: an address Chromium never resolves, answered from memory.
    internal const string DocumentUrl = "https://print.rask.invalid/";
#pragma warning restore S1075

    private const double MarginInches = 10 / 25.4;
    private static readonly TimeSpan CleanupWait = TimeSpan.FromSeconds(5);

    public static async Task<byte[]> Run(ChromiumProcess browser, string html, PdfPage page, CancellationToken cancellation)
    {
        var devTools = browser.Connection;
        var context = (await devTools.Send("Target.createBrowserContext", cancellation: cancellation).ConfigureAwait(false))
            .GetProperty("browserContextId").GetString()!;
        try
        {
            var target = (await devTools.Send(
                    "Target.createTarget",
                    json =>
                    {
                        json.WriteString("url", "about:blank");
                        json.WriteString("browserContextId", context);
                    },
                    cancellation: cancellation).ConfigureAwait(false))
                .GetProperty("targetId").GetString()!;
            var session = (await devTools.Send(
                    "Target.attachToTarget",
                    json =>
                    {
                        json.WriteString("targetId", target);
                        json.WriteBoolean("flatten", true);
                    },
                    cancellation: cancellation).ConfigureAwait(false))
                .GetProperty("sessionId").GetString()!;
            try
            {
                await Load(devTools, session, Encoding.UTF8.GetBytes(html), cancellation).ConfigureAwait(false);
                return await PrintToPdf(devTools, session, page, cancellation).ConfigureAwait(false);
            }
            finally
            {
                devTools.Forget(session);
            }
        }
        finally
        {
            await CloseContext(browser, context).ConfigureAwait(false);
        }
    }

    private static async Task Load(DevToolsConnection devTools, string session, byte[] document, CancellationToken cancellation)
    {
        // A load is matched to its navigation by loader id, so the blank page the tab opened with can never pass for
        // the document. The event can arrive before the navigation's own reply; whichever comes first makes the entry.
        var loads = new ConcurrentDictionary<string, TaskCompletionSource>(StringComparer.Ordinal);
        devTools.Listen(session, "Page.lifecycleEvent", lifecycle =>
        {
            if (string.Equals(lifecycle.GetProperty("name").GetString(), "load", StringComparison.Ordinal))
            {
                Loaded(loads, lifecycle.GetProperty("loaderId").GetString()!).TrySetResult();
            }
        });
        devTools.Listen(session, "Fetch.requestPaused", request => Answer(devTools, session, request, document));

        await devTools.Send("Page.enable", sessionId: session, cancellation: cancellation).ConfigureAwait(false);
        await devTools.Send("Page.setLifecycleEventsEnabled", json => json.WriteBoolean("enabled", true), session, cancellation)
            .ConfigureAwait(false);
        await devTools.Send("Emulation.setScriptExecutionDisabled", json => json.WriteBoolean("value", true), session, cancellation).ConfigureAwait(false);
        await devTools.Send(
            "Fetch.enable",
            json =>
            {
                json.WriteStartArray("patterns");
                json.WriteStartObject();
                json.WriteString("urlPattern", "*");
                json.WriteEndObject();
                json.WriteEndArray();
            },
            session,
            cancellation).ConfigureAwait(false);

        var navigated = await devTools.Send("Page.navigate", json => json.WriteString("url", DocumentUrl), session, cancellation).ConfigureAwait(false);
        if (navigated.TryGetProperty("errorText", out var error))
        {
            throw new InvalidOperationException($"Chromium could not load the document to print: {error.GetString()}");
        }

        await Loaded(loads, navigated.GetProperty("loaderId").GetString()!).Task.WaitAsync(cancellation).ConfigureAwait(false);
    }

    private static TaskCompletionSource Loaded(ConcurrentDictionary<string, TaskCompletionSource> loads, string loaderId) =>
        loads.GetOrAdd(loaderId, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    // Runs on the reading thread, so it answers without waiting for the reply.
    private static void Answer(DevToolsConnection devTools, string session, JsonElement request, byte[] document)
    {
        var id = request.GetProperty("requestId").GetString()!;
        var isDocument = string.Equals(request.GetProperty("request").GetProperty("url").GetString(), DocumentUrl, StringComparison.Ordinal);
        var answer = isDocument
            ? devTools.Send(
                "Fetch.fulfillRequest",
                json =>
                {
                    json.WriteString("requestId", id);
                    json.WriteNumber("responseCode", 200);
                    json.WriteStartArray("responseHeaders");
                    json.WriteStartObject();
                    json.WriteString("name", "Content-Type");
                    json.WriteString("value", "text/html; charset=utf-8");
                    json.WriteEndObject();
                    json.WriteEndArray();
                    json.WriteBase64String("body", document);
                },
                session)
            : devTools.Send(
                "Fetch.failRequest",
                json =>
                {
                    json.WriteString("requestId", id);
                    json.WriteString("errorReason", "BlockedByClient");
                },
                session);

        // Nobody awaits the answer; a failed one surfaces as the document never loading, which the timeout reports.
        _ = answer.ContinueWith(static task => task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    private static async Task<byte[]> PrintToPdf(DevToolsConnection devTools, string session, PdfPage page, CancellationToken cancellation)
    {
        var printed = await devTools.Send(
            "Page.printToPDF",
            json =>
            {
                json.WriteBoolean("landscape", page.IsLandscape);
                json.WriteBoolean("printBackground", true);
                // Chromium tags by default, and the structure tree is most of the file: a 209-page table came out
                // 12 MB tagged and 0.45 MB untagged, and printed in twice the time.
                json.WriteBoolean("generateTaggedPDF", false);
                json.WriteNumber("paperWidth", page.WidthInches);
                json.WriteNumber("paperHeight", page.HeightInches);
                json.WriteNumber("marginTop", MarginInches);
                json.WriteNumber("marginBottom", MarginInches);
                json.WriteNumber("marginLeft", MarginInches);
                json.WriteNumber("marginRight", MarginInches);
            },
            session,
            cancellation).ConfigureAwait(false);
        return printed.GetProperty("data").GetBytesFromBase64();
    }

    // The context goes even when the print was cancelled. A browser that cannot close it in five seconds has
    // stopped answering, and is ended so the next print starts a fresh one.
    private static async Task CloseContext(ChromiumProcess browser, string context)
    {
        using var closing = new CancellationTokenSource(CleanupWait);
        try
        {
            await browser.Connection.Send(
                "Target.disposeBrowserContext", json => json.WriteString("browserContextId", context), cancellation: closing.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            browser.Kill();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            // The browser is already gone; the next print starts another.
        }
    }
}
