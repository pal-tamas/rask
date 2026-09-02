using System.Reflection;

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
    /// The compiled CSS. Empty if the sheet did not ship, which leaves a surface unstyled rather than
    /// unstartable.
    /// </summary>
    /// <remarks>
    /// Read once into a static: the same bytes on every render, on every request, for the process's life.
    /// </remarks>
    public static string Css { get; } = Read();

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
