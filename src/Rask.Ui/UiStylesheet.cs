using System.Reflection;
using System.Text;

namespace Rask.Ui;

/// <summary>
/// The kit's compiled stylesheet, for a surface to inline.
/// </summary>
/// <remarks>
/// <para>
/// Tailwind is a compiler: it scans the project it runs in for the class names actually written, and this
/// kit ships as a compiled assembly. A consuming app's own Tailwind build cannot see these class names and
/// emits none of them — so the kit compiles its own sheet at its own build and hands it over here. Without
/// this the components render as unstyled HTML, and nothing reports it.
/// </para>
/// <para>
/// <b>Inline this BEFORE the app's own stylesheet.</b> The sheet defines the <c>--color-ui-*</c> tokens as
/// its palette, and redefining them in the app's own <c>@theme</c> is how a surface re-skins the kit
/// without overriding a single rule — which only works while the app's copy is the one the cascade reads
/// last. It carries no preflight and no <c>html</c>/<c>body</c> rules for the same reason: the app owns
/// its document, and a reset arriving from a library restyles pages that never asked for it.
/// </para>
/// <para>
/// Inlined rather than served: the alternative is a static web asset, which needs the Razor SDK and a
/// <c>_content/</c> path a host has to map. For a stylesheet this size a <c>&lt;style&gt;</c> is smaller
/// than the machinery, and it is what lets this package ship as a plain assembly with no assets at all.
/// </para>
/// </remarks>
public static class UiStylesheet
{
    /// <summary>
    /// The attribute that turns the kit's theme on for a subtree: <c>data-rask-ui</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Put it on <c>&lt;html&gt;</c> (via the root component's <c>Shell</c> override) to theme the whole
    /// page, or on any container to theme just that subtree. <b>Nothing in the kit has a colour until
    /// something in its ancestry carries this</b>, so a surface that forgets it renders structurally
    /// correct components with every colour computing to nothing.
    /// </para>
    /// <para>
    /// It exists because daisyUI's themes are defined at the document root by default, which would mean a
    /// referenced library repainting the background and text colour of an application that only wanted a
    /// button. Scoping the theme to this attribute keeps that an opt-in: the palette, and the
    /// <c>color-scheme</c> that comes with it, apply exactly where a surface asks for them.
    /// </para>
    /// <para>
    /// Switching themes needs no JavaScript. Inside the scope, daisyUI matches
    /// <c>input.theme-controller[value=dark]:checked</c> and an explicit <c>data-theme</c>, so a checkbox
    /// or a radio group changes the palette through CSS alone; with neither, the theme follows the
    /// operating system's <c>prefers-color-scheme</c>.
    /// </para>
    /// </remarks>
    public const string ThemeScopeAttribute = "data-rask-ui";

    /// <summary>
    /// The compiled CSS. Empty if the sheet did not ship, which leaves a surface unstyled rather than
    /// unstartable.
    /// </summary>
    /// <remarks>
    /// Read once into a static: the same bytes on every render, on every request, for the process's life.
    /// </remarks>
    public static string Css { get; } = Read();

    /// <summary>
    /// Where the build writes the sheet, relative to the app root: <c>/css/rask-ui.css</c>.
    /// </summary>
    /// <remarks>
    /// Matches <c>RaskUiStylesheetOutput</c> in the package's build targets. An app that moves the
    /// output moves this too, by passing its own href.
    /// </remarks>
    public const string Path = "/css/rask-ui.css";

    /// <summary>
    /// A cache-busting token for <see cref="Path" />: the sheet's own content hash.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file is meant to be cached hard, which makes staleness the failure to design against — an
    /// app that upgrades the kit and keeps serving the old bytes looks broken in a way nothing reports.
    /// A hash of the content changes exactly when the content does.
    /// </para>
    /// <para>
    /// Empty when the sheet did not ship, so the href stays a plain path rather than gaining a
    /// <c>?v=</c> with nothing after it.
    /// </para>
    /// </remarks>
    public static string Version { get; } = Hash(Css);

    /// <summary>
    /// <see cref="Path" /> with the cache-busting token, ready for a <c>&lt;link&gt;</c>.
    /// </summary>
    /// <param name="pathBase">The app's path base, for a deployment under a sub-path.</param>
    public static string Href(string? pathBase = null) =>
        Version.Length == 0
            ? (pathBase ?? string.Empty) + Path
            : (pathBase ?? string.Empty) + Path + "?v=" + Version;

    private static string Hash(string css)
    {
        if (css.Length == 0)
        {
            return string.Empty;
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(css));

        // Eight hex characters. This is a cache key, not a signature: it only has to change when the
        // bytes do, and a long one in every URL is noise in the markup.
        return Convert.ToHexStringLower(bytes)[..8];
    }

    private static string Read()
    {
        var assembly = typeof(UiStylesheet).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream("Rask.Ui.ui.css");

        // Empty rather than throwing: an unstyled surface still shows what is happening, and failing to
        // start a whole application because a stylesheet is missing would be the worse trade. The build
        // already refuses to pack without it (EmbedRaskUiStylesheet), so this path means a tampered
        // assembly rather than an ordinary mistake.
        if (stream is null)
        {
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
