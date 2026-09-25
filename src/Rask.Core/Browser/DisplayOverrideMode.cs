using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     A fallback display mode for <c>display_override</c>
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/Manifest/display_override" />). Supersets
///     <see cref="DisplayMode" /> with the override-only modes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DisplayOverrideMode>))]
public enum DisplayOverrideMode
{
    /// <summary>Standalone window (<c>standalone</c>).</summary>
    [JsonStringEnumMemberName("standalone")] Standalone,

    /// <summary>Full screen (<c>fullscreen</c>).</summary>
    [JsonStringEnumMemberName("fullscreen")] Fullscreen,

    /// <summary>Standalone plus minimal navigation UI (<c>minimal-ui</c>).</summary>
    [JsonStringEnumMemberName("minimal-ui")] MinimalUi,

    /// <summary>A normal browser tab (<c>browser</c>).</summary>
    [JsonStringEnumMemberName("browser")] Browser,

    /// <summary>Title-bar area is given to the app (<c>window-controls-overlay</c>, desktop PWAs).</summary>
    [JsonStringEnumMemberName("window-controls-overlay")] WindowControlsOverlay,

    /// <summary>Tabbed application mode (<c>tabbed</c>).</summary>
    [JsonStringEnumMemberName("tabbed")] Tabbed
}
