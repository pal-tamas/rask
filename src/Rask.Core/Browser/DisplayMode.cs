using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     How an installed PWA is displayed (<c>display</c> member of the web app manifest,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/Manifest/display" />).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DisplayMode>))]
public enum DisplayMode
{
    /// <summary>Standalone window, no browser chrome — the usual "feels like an app" mode.</summary>
    [JsonStringEnumMemberName("standalone")] Standalone,

    /// <summary>Full screen, no chrome at all.</summary>
    [JsonStringEnumMemberName("fullscreen")] Fullscreen,

    /// <summary>Standalone plus a minimal navigation UI (back/reload).</summary>
    [JsonStringEnumMemberName("minimal-ui")] MinimalUi,

    /// <summary>A normal browser tab (not installed-feeling).</summary>
    [JsonStringEnumMemberName("browser")] Browser
}
