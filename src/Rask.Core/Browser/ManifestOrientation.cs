using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     Preferred orientation of an installed app (<c>orientation</c> member of the web app manifest,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/Manifest/orientation" />).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ManifestOrientation>))]
public enum ManifestOrientation
{
    /// <summary>No preference (<c>any</c>).</summary>
    [JsonStringEnumMemberName("any")] Any,

    /// <summary>The device's natural orientation (<c>natural</c>).</summary>
    [JsonStringEnumMemberName("natural")] Natural,

    /// <summary>Either portrait orientation (<c>portrait</c>).</summary>
    [JsonStringEnumMemberName("portrait")] Portrait,

    /// <summary>Primary portrait (<c>portrait-primary</c>).</summary>
    [JsonStringEnumMemberName("portrait-primary")] PortraitPrimary,

    /// <summary>Secondary portrait (<c>portrait-secondary</c>).</summary>
    [JsonStringEnumMemberName("portrait-secondary")] PortraitSecondary,

    /// <summary>Either landscape orientation (<c>landscape</c>).</summary>
    [JsonStringEnumMemberName("landscape")] Landscape,

    /// <summary>Primary landscape (<c>landscape-primary</c>).</summary>
    [JsonStringEnumMemberName("landscape-primary")] LandscapePrimary,

    /// <summary>Secondary landscape (<c>landscape-secondary</c>).</summary>
    [JsonStringEnumMemberName("landscape-secondary")] LandscapeSecondary
}
