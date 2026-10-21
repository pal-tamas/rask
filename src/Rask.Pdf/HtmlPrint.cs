using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Rask.Printing;

/// <summary>
/// Prints one document in a browser context of its own — no cookie, storage or cache shared with any other
/// document — and closes the context after. The page reaches nothing but the <see cref="DocumentServer"/>, and its
/// scripts do not run.
/// </summary>
internal static class HtmlPrint
{
    private static readonly TimeSpan CleanupWait = TimeSpan.FromSeconds(5);

    public static async Task<byte[]> Run(
        ChromiumProcess browser, PrintDocument document, int maxPages, PrintProgress progress, CancellationToken cancellation)
    {
        var devTools = browser.Connection;
        progress.Now("creating its browser context");
        var context = (await devTools.Send("Target.createBrowserContext", cancellation: cancellation).ConfigureAwait(false))
            .GetProperty("browserContextId").GetString()!;
        try
        {
            progress.Now("opening its tab");
            var target = (await devTools.Send(
                    "Target.createTarget",
                    json =>
                    {
                        json.WriteString("url", "about:blank");
                        json.WriteString("browserContextId", context);
                    },
                    cancellation: cancellation).ConfigureAwait(false))
                .GetProperty("targetId").GetString()!;
            progress.Now("attaching to its tab");
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
                progress.Now("loading the document (its load event)");
                await Load(devTools, session, new DocumentServer(devTools, session, document, progress, cancellation), cancellation)
                    .ConfigureAwait(false);
                progress.Now("waiting for its web fonts");
                await FontsReady(devTools, session, cancellation).ConfigureAwait(false);
                progress.Now("printing it");
                return await PrintToPdf(devTools, session, document.Options, maxPages, cancellation).ConfigureAwait(false);
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

    private static async Task Load(DevToolsConnection devTools, string session, DocumentServer server, CancellationToken cancellation)
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
        devTools.Listen(session, "Fetch.requestPaused", server.Answer);

        await devTools.Send("Page.enable", sessionId: session, cancellation: cancellation).ConfigureAwait(false);
        await devTools.Send("Page.setLifecycleEventsEnabled", json => json.WriteBoolean("enabled", true), session, cancellation)
            .ConfigureAwait(false);
        await devTools.Send("Emulation.setScriptExecutionDisabled", json => json.WriteBoolean("value", true), session, cancellation)
            .ConfigureAwait(false);
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

        var navigated = await devTools.Send("Page.navigate", json => json.WriteString("url", server.DocumentUrl), session, cancellation)
            .ConfigureAwait(false);
        if (navigated.TryGetProperty("errorText", out var error))
        {
            throw new InvalidOperationException($"Chromium could not load the document to print: {error.GetString()}");
        }

        await Loaded(loads, navigated.GetProperty("loaderId").GetString()!).Task.WaitAsync(cancellation).ConfigureAwait(false);
    }

    private static TaskCompletionSource Loaded(ConcurrentDictionary<string, TaskCompletionSource> loads, string loaderId) =>
        loads.GetOrAdd(loaderId, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    // The load event does not wait for a web font the layout asks for late. The page's own scripts stay off; this
    // expression is the protocol's, evaluated in the page, and only waits.
    private static Task<JsonElement> FontsReady(DevToolsConnection devTools, string session, CancellationToken cancellation) => devTools.Send(
        "Runtime.evaluate",
        json =>
        {
            json.WriteString("expression", "document.fonts.ready.then(() => true)");
            json.WriteBoolean("awaitPromise", true);
            json.WriteBoolean("returnByValue", true);
        },
        session,
        cancellation);

    private static async Task<byte[]> PrintToPdf(
        DevToolsConnection devTools, string session, PdfOptions options, int maxPages, CancellationToken cancellation)
    {
        var printed = await devTools.Send("Page.printToPDF", json => PrintLayout.Write(json, options, maxPages + 1), session, cancellation)
            .ConfigureAwait(false);
        var pdf = printed.GetProperty("data").GetBytesFromBase64();
        return PdfPageCount.Of(pdf) <= maxPages
            ? pdf
            : throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The document runs to more than {maxPages} pages, the most Rask:Pdf:MaxPages allows to print."));
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
