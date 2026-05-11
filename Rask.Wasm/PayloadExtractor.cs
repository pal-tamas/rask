using System.Text.Json;

namespace Rask.Wasm;

internal static class PayloadExtractor
{
    public static Result Extract(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return new Result(string.Empty, null, null, null);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var html = root.TryGetProperty("html", out var h) && h.ValueKind == JsonValueKind.String
            ? h.GetString() ?? string.Empty
            : string.Empty;
        var cssHash = root.TryGetProperty("cssHash", out var ch) && ch.ValueKind == JsonValueKind.String
            ? ch.GetString()
            : null;
        var cssText = root.TryGetProperty("cssText", out var ct) && ct.ValueKind == JsonValueKind.String
            ? ct.GetString()
            : null;
        string? historyJson = null;
        if (root.TryGetProperty("history", out var hist) && hist.ValueKind == JsonValueKind.Object)
        {
            historyJson = hist.GetRawText();
        }

        return new Result(html, cssHash, cssText, historyJson);
    }

    public readonly record struct Result(string Html, string? CssHash, string? CssText, string? HistoryJson);
}
