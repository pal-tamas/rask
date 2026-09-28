using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>An artwork image for the media session (an entry of <c>MediaMetadata.artwork</c>).</summary>
/// <param name="Src">Image URL.</param>
/// <param name="Sizes">Space-separated sizes, e.g. <c>"512x512"</c> (optional).</param>
/// <param name="Type">MIME type, e.g. <c>"image/png"</c> (optional).</param>
public sealed record MediaArtwork(
    string Src,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sizes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type = null);
