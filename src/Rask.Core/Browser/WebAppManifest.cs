using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

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
    // Settable rather than init-only, so the Pwa battery can hand this to a configure delegate
    // (c.Pwa.Configure(m => m.Name = "Shop")) the way every other battery configures its options.
    // Records with init-only members cannot be mutated by an Action<T>, and a `with` expression at
    // that call site would have been a concept to learn for no gain. init -> set is source-compatible:
    // every existing object initializer still compiles.

    /// <summary>Full app name shown on the install prompt / splash screen.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    /// <summary>Short name shown under the home-screen icon.</summary>
    [JsonPropertyName("short_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShortName { get; set; }

    /// <summary>Optional description.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>Where the app opens when launched (default <c>"."</c> — the app root).</summary>
    [JsonPropertyName("start_url")]
    public string StartUrl { get; set; } = ".";

    /// <summary>Navigation scope the installed app controls (default <c>"."</c>).</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = ".";

    /// <summary>Display mode (default <see cref="DisplayMode.Standalone" />).</summary>
    [JsonPropertyName("display")]
    public DisplayMode Display { get; set; } = DisplayMode.Standalone;

    /// <summary>Theme color (also emitted as <c>&lt;meta name="theme-color"&gt;</c>).</summary>
    [JsonPropertyName("theme_color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThemeColor { get; set; }

    /// <summary>Background color of the splash screen.</summary>
    [JsonPropertyName("background_color")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BackgroundColor { get; set; }

    /// <summary>Home-screen / install icons. At least one ~192px and one ~512px icon is recommended.</summary>
    [JsonPropertyName("icons")]
    public IReadOnlyList<ManifestIcon> Icons { get; set; } = [];

    /// <summary>App category hints for stores/launchers, e.g. <c>["productivity", "utilities"]</c>.</summary>
    [JsonPropertyName("categories")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Categories { get; set; }

    /// <summary>Preferred orientation when installed (unset = no preference).</summary>
    [JsonPropertyName("orientation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ManifestOrientation? Orientation { get; set; }

    /// <summary>Ordered fallback display modes tried before <see cref="Display" /> (e.g. window-controls-overlay).</summary>
    [JsonPropertyName("display_override")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DisplayOverrideMode>? DisplayOverride { get; set; }

    /// <summary>Home-screen / jump-list shortcuts into specific app sections.</summary>
    [JsonPropertyName("shortcuts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ManifestShortcut>? Shortcuts { get; set; }

    /// <summary>Screenshots shown in the richer install / app-store UI.</summary>
    [JsonPropertyName("screenshots")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ManifestScreenshot>? Screenshots { get; set; }

    /// <summary>Registers the app as an OS share target (receive shared title/text/url).</summary>
    [JsonPropertyName("share_target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShareTarget? ShareTarget { get; set; }

    /// <summary>File-type associations so the OS can launch the app to open matching files.</summary>
    [JsonPropertyName("file_handlers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FileHandler>? FileHandlers { get; set; }

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
        var root = basePath switch
        {
            null or "" => "/",
            _ when basePath.EndsWith('/') => basePath,
            _ => basePath + "/",
        };
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

    // Never fetched: only there so a relative path has something absolute to resolve against.
    private const string PlaceholderOrigin = "https://_";

    /// <summary>Resolves <paramref name="url" /> against <paramref name="root" />, mirroring <c>new URL(url, root)</c>.</summary>
    private static string? Resolve(string url, string root)
    {
        if (string.IsNullOrEmpty(url) || url.StartsWith('/') || url.Contains("://", StringComparison.Ordinal))
        {
            return null; // already absolute / host-rooted — leave untouched (matches the WASM abs())
        }

        // A placeholder origin lets Uri do the "." / nested-segment resolution; we keep only the path.
        var resolved = new Uri(new Uri(PlaceholderOrigin + root, UriKind.Absolute), url);
        return resolved.PathAndQuery;
    }
}
