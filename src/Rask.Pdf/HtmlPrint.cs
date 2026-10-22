using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Rask.Printing;

/// <summary>
/// Prints one document in a browser context of its own — no cookie, storage or cache shared with any other
/// document — and closes the context after. The page reaches nothing but the <see cref="DocumentServer"/>, its
/// scripts do not run, and it is drawn once, after it has loaded (<see cref="PrintHold"/>).
/// </summary>
internal static class HtmlPrint
{
    private static readonly TimeSpan CleanupWait = TimeSpan.FromSeconds(5);

    // Chromium's whole answer when its renderer could not make the PDF; it says no more, on the pipe or in its log.
    private const string PrintingFailed = "Printing failed";

    // How far Chromium shrinks a document wider than its page, at most (Blink's printing maximum shrink factor).
    private const int MostShrink = 2;

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
                progress.Now("drawing it and waiting for its web fonts");
                var pageHeight = await Drawn(devTools, session, document.Options, cancellation).ConfigureAwait(false);
                progress.Now("cutting its long tables at their pages");
                var cut = await Evaluate(devTools, session, PrintTables.Cut(pageHeight), awaitPromise: false, cancellation).ConfigureAwait(false);
                progress.TablesCut = Number(cut, otherwise: 0);
                progress.Now("printing it");
                return await PrintToPdf(devTools, session, document, maxPages, cancellation).ConfigureAwait(false);
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

    // The document loaded held out of rendering (PrintHold). It is drawn once, in the print media printToPDF lays it
    // out in and at the page's width — a screen layout first would be thrown away — and the web fonts that drawing
    // asked for are waited for: the load event does not wait for a font the layout asks for late. A document wider
    // than the page is shrunk to fit by Chromium, by up to half, laid out that much wider and longer: so is this one,
    // and the page height it answers is in that layout's pixels. The page's own scripts stay off; the release is the
    // protocol's, evaluated in the page.
    private static async Task<double> Drawn(DevToolsConnection devTools, string session, PdfOptions options, CancellationToken cancellation)
    {
        await devTools.Send("Emulation.setEmulatedMedia", json => json.WriteString("media", "print"), session, cancellation)
            .ConfigureAwait(false);
        var (width, height) = PrintLayout.Content(options);
        var pageWidth = (int)Math.Floor(width);
        await Viewport(devTools, session, pageWidth, height, cancellation).ConfigureAwait(false);
        var drawn = await Evaluate(devTools, session, PrintHold.Release, awaitPromise: true, cancellation).ConfigureAwait(false);
        var documentWidth = Number(drawn, otherwise: pageWidth);
        if (documentWidth <= pageWidth)
        {
            return height;
        }

        var shrunkWidth = Math.Min(documentWidth, pageWidth * MostShrink);
        var shrunkHeight = height * shrunkWidth / pageWidth;
        await Viewport(devTools, session, shrunkWidth, shrunkHeight, cancellation).ConfigureAwait(false);
        return shrunkHeight;
    }

    private static int Number(JsonElement result, int otherwise) =>
        result.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : otherwise;

    private static Task<JsonElement> Viewport(DevToolsConnection devTools, string session, int width, double height, CancellationToken cancellation) =>
        devTools.Send(
            "Emulation.setDeviceMetricsOverride",
            json =>
            {
                json.WriteNumber("width", width);
                json.WriteNumber("height", (int)Math.Ceiling(height));
                json.WriteNumber("deviceScaleFactor", 1);
                json.WriteBoolean("mobile", false);
            },
            session,
            cancellation);

    private static async Task<JsonElement> Evaluate(
        DevToolsConnection devTools, string session, string expression, bool awaitPromise, CancellationToken cancellation)
    {
        var evaluated = await devTools.Send(
            "Runtime.evaluate",
            json =>
            {
                json.WriteString("expression", expression);
                json.WriteBoolean("awaitPromise", awaitPromise);
                json.WriteBoolean("returnByValue", true);
            },
            session,
            cancellation).ConfigureAwait(false);
        return evaluated.GetProperty("result");
    }

    private static async Task<byte[]> PrintToPdf(
        DevToolsConnection devTools, string session, PrintDocument document, int maxPages, CancellationToken cancellation)
    {
        JsonElement printed;
        try
        {
            printed = await devTools.Send(
                    "Page.printToPDF", json => PrintLayout.Write(json, document.Options, document.PageNumbers, maxPages + 1), session, cancellation)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException e) when (e.Message.EndsWith(PrintingFailed, StringComparison.Ordinal))
        {
            throw Named(e);
        }

        var pdf = printed.GetProperty("data").GetBytesFromBase64();
        return PdfPageCount.Of(pdf) <= maxPages
            ? pdf
            : throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The document runs to more than {maxPages} pages, the most Rask:Pdf:MaxPages allows to print."));
    }

    /// <summary>Chromium's bare "Printing failed", as what it most likely means and what to do about it.</summary>
    internal static InvalidOperationException Named(InvalidOperationException refusal) => new(
        "Chromium could not print the document (\"Printing failed\"): its renderer ran out of room laying it out. That is "
        + "a document of many hundreds of pages, or a long table Rask could not cut at its pages (one with a rowspan, its "
        + "own <colgroup>, or a row taller than a page) — laying one table out across hundreds of pages costs Chromium "
        + "memory for every row on every page. Print it in parts (a PDF per month, say), split the table into several, or "
        + "print it smaller so it takes fewer pages.",
        refusal);

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
