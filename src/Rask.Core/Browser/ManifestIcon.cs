using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>An icon entry in the web app manifest (<c>icons[]</c>).</summary>
/// <param name="Src">Icon URL (relative URLs resolve against the page when applied).</param>
/// <param name="Sizes">Space-separated sizes, e.g. <c>"192x192 512x512"</c> or <c>"any"</c> for SVG.</param>
/// <param name="Type">MIME type, e.g. <c>"image/png"</c> or <c>"image/svg+xml"</c>.</param>
/// <param name="Purpose">Optional purpose, e.g. <c>"any"</c>, <c>"maskable"</c>, or <c>"any maskable"</c>.</param>
public sealed record ManifestIcon(
    [property: JsonPropertyName("src")] string Src,
    [property: JsonPropertyName("sizes")] string Sizes,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("purpose"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Purpose = null);
