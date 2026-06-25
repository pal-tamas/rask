using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Wasm.Browser;

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

/// <summary>
///     A typed <see href="https://developer.mozilla.org/en-US/docs/Web/Manifest">web app manifest</see>.
///     Configure it in <c>Program.cs</c> with <c>WasmHostBuilder.UseManifest(...)</c>; the framework emits
///     the <c>&lt;link rel="manifest"&gt;</c> and <c>&lt;meta name="theme-color"&gt;</c> at boot, so you
///     don't hand-write <c>manifest.webmanifest</c>. Relative URLs (<see cref="StartUrl" />,
///     <see cref="Scope" />, icon <c>src</c>) are resolved against the page, so they stay correct under a
///     sub-path deploy (e.g. GitHub Pages).
/// </summary>
public sealed record WebAppManifest
{
    /// <summary>Full app name shown on the install prompt / splash screen.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Short name shown under the home-screen icon.</summary>
    [JsonPropertyName("short_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortName { get; init; }

    /// <summary>Optional description.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>Where the app opens when launched (default <c>"."</c> — the app root).</summary>
    [JsonPropertyName("start_url")]
    public string StartUrl { get; init; } = ".";

    /// <summary>Navigation scope the installed app controls (default <c>"."</c>).</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = ".";

    /// <summary>Display mode (default <see cref="DisplayMode.Standalone" />).</summary>
    [JsonPropertyName("display")]
    public DisplayMode Display { get; init; } = DisplayMode.Standalone;

    /// <summary>Theme color (also emitted as <c>&lt;meta name="theme-color"&gt;</c>).</summary>
    [JsonPropertyName("theme_color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThemeColor { get; init; }

    /// <summary>Background color of the splash screen.</summary>
    [JsonPropertyName("background_color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundColor { get; init; }

    /// <summary>Home-screen / install icons. At least one ~192px and one ~512px icon is recommended.</summary>
    [JsonPropertyName("icons")]
    public IReadOnlyList<ManifestIcon> Icons { get; init; } = [];

    /// <summary>Serializes this manifest to its JSON form (omitting unset members).</summary>
    public string ToJson() => JsonSerializer.Serialize(this, RaskManifestJsonContext.Default.WebAppManifest);
}

/// <summary>Source-generated, trim-safe JSON metadata for <see cref="WebAppManifest" />.</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(WebAppManifest))]
internal sealed partial class RaskManifestJsonContext : JsonSerializerContext;
