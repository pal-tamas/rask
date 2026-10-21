using System.Text;
using System.Text.Json;

namespace Rask.Printing;

/// <summary>
/// Answers every request the printed page makes, from memory: the document itself, then the app's own static files
/// under the same origin. Anything else — another host, a POST, a file the app does not have — is refused, so a
/// print never reaches the network.
/// </summary>
internal sealed class DocumentServer(DevToolsConnection devTools, string session, PrintDocument document, CancellationToken cancellation)
{
    /// <summary>The origin the document is served under. <c>.invalid</c> never resolves, so nothing can leave the machine.</summary>
#pragma warning disable S1075 // not a place to configure: an address Chromium never resolves, answered from memory.
    internal const string Origin = "https://print.rask.invalid";
#pragma warning restore S1075

    private readonly byte[] _html = Encoding.UTF8.GetBytes(document.Html);

    /// <summary>Where the document answers: the origin plus its own path.</summary>
    public string DocumentUrl { get; } = Origin + (document.Path.StartsWith('/') ? document.Path : "/" + document.Path);

    /// <summary>Called on the reading thread for each <c>Fetch.requestPaused</c>; answers without blocking it.</summary>
    public void Answer(JsonElement paused)
    {
        var id = paused.GetProperty("requestId").GetString()!;
        var request = paused.GetProperty("request");
        var url = request.GetProperty("url").GetString() ?? string.Empty;
        var isGet = string.Equals(request.GetProperty("method").GetString(), "GET", StringComparison.Ordinal);

        if (isGet && string.Equals(url, DocumentUrl, StringComparison.Ordinal))
        {
            Observe(Fulfill(id, _html, "text/html; charset=utf-8"));
        }
        else if (isGet && document.Assets is { } assets && OwnPath(url) is { } path)
        {
            Observe(Task.Run(() => Serve(id, assets, path), cancellation));
        }
        else
        {
            Observe(Refuse(id));
        }
    }

    private static string? OwnPath(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && string.Equals(uri.GetLeftPart(UriPartial.Authority), Origin, StringComparison.OrdinalIgnoreCase)
            ? Uri.UnescapeDataString(uri.AbsolutePath)
            : null;

    private async Task Serve(string id, IPrintAssets assets, string path)
    {
        var asset = await assets.Find(path, cancellation).ConfigureAwait(false);
        await (asset is null ? NotFound(id) : Fulfill(id, asset.Body, asset.ContentType)).ConfigureAwait(false);
    }

    private Task<JsonElement> Fulfill(string id, byte[] body, string contentType) => devTools.Send(
        "Fetch.fulfillRequest",
        json =>
        {
            json.WriteString("requestId", id);
            json.WriteNumber("responseCode", 200);
            json.WriteStartArray("responseHeaders");
            json.WriteStartObject();
            json.WriteString("name", "Content-Type");
            json.WriteString("value", contentType);
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteBase64String("body", body);
        },
        session);

    private Task<JsonElement> NotFound(string id) => devTools.Send(
        "Fetch.fulfillRequest",
        json =>
        {
            json.WriteString("requestId", id);
            json.WriteNumber("responseCode", 404);
        },
        session);

    private Task<JsonElement> Refuse(string id) => devTools.Send(
        "Fetch.failRequest",
        json =>
        {
            json.WriteString("requestId", id);
            json.WriteString("errorReason", "BlockedByClient");
        },
        session);

    // Nobody awaits an answer; one that fails leaves its request hanging, and the document's timeout reports it.
    private static void Observe(Task answer) =>
        _ = answer.ContinueWith(static task => task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
}
