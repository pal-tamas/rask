using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>A home-screen shortcut / jump-list entry (<c>shortcuts[]</c>).</summary>
/// <param name="Name">Label shown in the shortcut menu.</param>
/// <param name="Url">URL opened by the shortcut (resolved against the page when applied).</param>
/// <param name="ShortName">Optional shorter label.</param>
/// <param name="Description">Optional accessible description.</param>
/// <param name="Icons">Optional icons for the shortcut.</param>
public sealed record ManifestShortcut(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("short_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ShortName = null,
    [property: JsonPropertyName("description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description = null,
    [property: JsonPropertyName("icons"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ManifestIcon>? Icons = null);

/// <summary>A screenshot shown in the install/app-store UI (<c>screenshots[]</c>).</summary>
/// <param name="Src">Image URL.</param>
/// <param name="Sizes">Space-separated sizes, e.g. <c>"1280x720"</c>.</param>
/// <param name="Type">MIME type, e.g. <c>"image/png"</c>.</param>
/// <param name="FormFactor">Target form factor: <c>"wide"</c> (desktop) or <c>"narrow"</c> (mobile).</param>
/// <param name="Label">Accessible label.</param>
public sealed record ManifestScreenshot(
    [property: JsonPropertyName("src")] string Src,
    [property: JsonPropertyName("sizes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sizes = null,
    [property: JsonPropertyName("type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type = null,
    [property: JsonPropertyName("form_factor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FormFactor = null,
    [property: JsonPropertyName("label"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Label = null);

/// <summary>The query parameter names a share target maps the shared data onto (<c>share_target.params</c>).</summary>
/// <param name="Title">Form field that receives the shared title.</param>
/// <param name="Text">Form field that receives the shared text.</param>
/// <param name="Url">Form field that receives the shared URL.</param>
public sealed record ShareTargetParams(
    [property: JsonPropertyName("title"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title = null,
    [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null);

/// <summary>
///     Registers the installed app as a share target (<c>share_target</c>,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/Manifest/share_target" />) so the OS share
///     sheet can hand content to it.
/// </summary>
/// <param name="Action">URL the shared data is delivered to (resolved against the page when applied).</param>
/// <param name="Params">Maps the shared title/text/url onto query/form fields.</param>
/// <param name="Method">HTTP method, <c>"GET"</c> (default) or <c>"POST"</c>.</param>
/// <param name="Enctype">Encoding for POST, e.g. <c>"multipart/form-data"</c>.</param>
public sealed record ShareTarget(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("params")] ShareTargetParams Params,
    [property: JsonPropertyName("method"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Method = null,
    [property: JsonPropertyName("enctype"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Enctype = null);

/// <summary>
///     Associates file types with the installed app (an entry of <c>file_handlers[]</c>,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/Manifest/file_handlers" />) so the OS can
///     launch it to open matching files.
/// </summary>
/// <param name="Action">URL launched to handle the files (resolved against the page when applied).</param>
/// <param name="Accept">MIME type → file extensions map, e.g. <c>{ ["text/csv"] = [".csv"] }</c>.</param>
public sealed record FileHandler(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("accept")] IReadOnlyDictionary<string, string[]> Accept);

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
///     Configure it in <c>Program.cs</c> with <c>WasmHostBuilder.UsePwa(...)</c> (WASM) or
///     <c>AddRaskPwa(...)</c> (Server); the framework emits the <c>&lt;link rel="manifest"&gt;</c> and
///     <c>&lt;meta name="theme-color"&gt;</c> for you, so you don't hand-write <c>manifest.webmanifest</c>.
///     Relative URLs (<see cref="StartUrl" />, <see cref="Scope" />, icon <c>src</c>) stay correct under a
///     sub-path deploy (e.g. GitHub Pages): the WASM host resolves them against the page at boot, and the
///     Server host roots them at its base path via <see cref="ToJson(string)" />.
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

    /// <summary>App category hints for stores/launchers, e.g. <c>["productivity", "utilities"]</c>.</summary>
    [JsonPropertyName("categories")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Categories { get; init; }

    /// <summary>Preferred orientation when installed (unset = no preference).</summary>
    [JsonPropertyName("orientation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ManifestOrientation? Orientation { get; init; }

    /// <summary>Ordered fallback display modes tried before <see cref="Display" /> (e.g. window-controls-overlay).</summary>
    [JsonPropertyName("display_override")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DisplayOverrideMode>? DisplayOverride { get; init; }

    /// <summary>Home-screen / jump-list shortcuts into specific app sections.</summary>
    [JsonPropertyName("shortcuts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ManifestShortcut>? Shortcuts { get; init; }

    /// <summary>Screenshots shown in the richer install / app-store UI.</summary>
    [JsonPropertyName("screenshots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ManifestScreenshot>? Screenshots { get; init; }

    /// <summary>Registers the app as an OS share target (receive shared title/text/url).</summary>
    [JsonPropertyName("share_target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShareTarget? ShareTarget { get; init; }

    /// <summary>File-type associations so the OS can launch the app to open matching files.</summary>
    [JsonPropertyName("file_handlers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FileHandler>? FileHandlers { get; init; }

    /// <summary>Serializes this manifest to its JSON form (omitting unset members).</summary>
    /// <remarks>
    ///     Relative URLs (<see cref="StartUrl" />, <see cref="Scope" />, icon <c>src</c>, …) are left
    ///     verbatim — the WASM host resolves them against the page's <c>&lt;base&gt;</c> at boot. When the
    ///     manifest is served from its own URL (the Server host), use <see cref="ToJson(string)" /> so those
    ///     relative URLs are rooted at the app's base path instead.
    /// </remarks>
    public string ToJson() => JsonSerializer.Serialize(this, RaskManifestJsonContext.Default.WebAppManifest);

    /// <summary>
    ///     Serializes this manifest to JSON with all relative URLs rewritten to <paramref name="basePath" />-rooted
    ///     absolute paths. A web app manifest's URL members resolve relative to the <em>manifest's</em> URL, so a
    ///     manifest served from a dedicated endpoint (rather than injected into the page) must carry absolute paths
    ///     or <c>start_url</c>/<c>scope</c>/icons would resolve against the endpoint path and break. This is the
    ///     server-side analogue of the WASM host's boot-time <c>abs()</c> step.
    /// </summary>
    /// <param name="basePath">
    ///     The app's base path (e.g. the Server host's <c>PathBase</c>): <c>""</c> for a root deploy or
    ///     <c>"/app"</c> for a sub-path deploy. Absolute URLs (scheme-qualified, protocol-relative, or already
    ///     rooted at <c>/</c>) are left untouched.
    /// </param>
    public string ToJson(string basePath)
    {
        var root = string.IsNullOrEmpty(basePath) ? "/" : basePath.EndsWith('/') ? basePath : basePath + "/";
        var node = JsonNode.Parse(ToJson())!.AsObject();

        Reroot(node, "start_url", root);
        Reroot(node, "scope", root);
        RerootArraySrc(node, "icons", root);
        RerootArraySrc(node, "screenshots", root);
        if (node["shortcuts"] is JsonArray shortcuts)
        {
            foreach (var shortcut in shortcuts.OfType<JsonObject>())
            {
                Reroot(shortcut, "url", root);
                RerootArraySrc(shortcut, "icons", root);
            }
        }

        if (node["share_target"] is JsonObject shareTarget)
        {
            Reroot(shareTarget, "action", root);
        }

        if (node["file_handlers"] is JsonArray handlers)
        {
            foreach (var handler in handlers.OfType<JsonObject>())
            {
                Reroot(handler, "action", root);
            }
        }

        return node.ToJsonString();
    }

    private static void RerootArraySrc(JsonObject parent, string arrayKey, string root)
    {
        if (parent[arrayKey] is JsonArray array)
        {
            foreach (var item in array.OfType<JsonObject>())
            {
                Reroot(item, "src", root);
            }
        }
    }

    private static void Reroot(JsonObject obj, string key, string root)
    {
        if (obj[key]?.GetValue<string>() is { } url && Resolve(url, root) is { } resolved)
        {
            obj[key] = resolved;
        }
    }

    /// <summary>Resolves <paramref name="url" /> against <paramref name="root" />, mirroring <c>new URL(url, root)</c>.</summary>
    private static string? Resolve(string url, string root)
    {
        if (string.IsNullOrEmpty(url) || url.StartsWith('/') || url.Contains("://", StringComparison.Ordinal))
        {
            return null; // already absolute / host-rooted — leave untouched (matches the WASM abs())
        }

        // A placeholder authority lets Uri do the "." / nested-segment resolution; we keep only the path.
        var resolved = new Uri(new Uri("http://_" + root, UriKind.Absolute), url);
        return resolved.PathAndQuery;
    }
}

/// <summary>Source-generated, trim-safe JSON metadata for <see cref="WebAppManifest" />.</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(WebAppManifest))]
internal sealed partial class RaskManifestJsonContext : JsonSerializerContext;
