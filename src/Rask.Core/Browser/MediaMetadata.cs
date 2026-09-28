using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     Now-playing metadata shown by the OS (lock screen, media hub, smart-watch) — the
///     <c>MediaMetadata</c> of the <see href="https://developer.mozilla.org/en-US/docs/Web/API/MediaSession">
///     Media Session API</see>.
/// </summary>
public sealed record MediaMetadata
{
    /// <summary>Track / content title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    /// <summary>Artist / author / performer.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Artist { get; init; }

    /// <summary>Album / collection name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Album { get; init; }

    /// <summary>Artwork images (the OS picks the best-fitting size).</summary>
    public IReadOnlyList<MediaArtwork> Artwork { get; init; } = [];
}
