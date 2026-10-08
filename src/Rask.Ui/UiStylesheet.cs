using System.Reflection;
using System.Text;

namespace Rask;

/// <summary>
/// The kit's precompiled stylesheet, for a surface that has no Tailwind build of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>An app that runs Tailwind does not use this.</b> It takes the kit in with one line in its own
/// stylesheet — <c>@import "./vendor/rask-ui.css";</c> — and compiles ONE sheet: Tailwind, the kit's
/// theme and <c>dark</c> variant, and the classes the kit's components write beside its own. Linking
/// this sheet next to an app's own Tailwind output puts two <c>@layer utilities</c> in one document,
/// ranked by link order alone, where a base utility the app wrote beats a <c>dark:</c> variant of the kit's;
/// the build refuses <c>RaskUiWriteStylesheet</c> in such a project.
/// </para>
/// <para>
/// What is here is the kit compiled over its own sources at its own build: every class a kit component
/// can write, and nothing an app writes itself. It is for a panel drawn inside somebody else's page
/// (<c>Rask.DevTools</c> inlines <see cref="Css" />), or an app that draws only with <c>Ui.*</c>
/// components and links <see cref="Href" />.
/// </para>
/// <para>
/// It carries no preflight and no <c>html</c>/<c>body</c> rules: it lands in documents it does not own,
/// and a reset arriving from a library restyles pages that never asked for it.
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
    /// Dark mode is <see cref="UiAppearanceScript" />'s: a <c>dark</c> class on <c>&lt;html&gt;</c>. A scope
    /// with no <c>data-theme</c> and no script follows the operating system's <c>prefers-color-scheme</c>.
    /// </para>
    /// </remarks>
    public const string ThemeScopeAttribute = "data-rask-ui";

    /// <summary>
    /// The attribute a document drawn with this kit ALONE writes on its <c>&lt;html&gt;</c>.
    /// </summary>
    /// <remarks>
    /// An application runs its own Tailwind and has its own reset and its own classes for the page's ground.
    /// A document whose only sheet is this one — the operator console, the DevTools panel — has neither, and
    /// cannot write a class the sheet would know. This gives it both: Tailwind's preflight, and what every Flux
    /// layout writes by hand on the body, the sidebar and the header.
    /// </remarks>
    public const string DocumentAttribute = "data-rask-ui-document";

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
