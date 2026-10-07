namespace Rask;

/// <summary>
///     Where the editor's engine is served from: the one script <see cref="UiEditor" /> loads, and only when an
///     editor is on the page.
/// </summary>
/// <remarks>
///     The engine is Tiptap and ProseMirror, some 400 KB before compression, so it is neither in Rask's runtime
///     nor in this assembly: it is a static file every app's build copies into <c>wwwroot</c>
///     (<c>build/Rask.Ui.targets</c>, no setting asked for), and the browser fetches it the first time an editor
///     mounts. <c>Resources/editor/build.mjs</c> builds it and writes
///     <see cref="Version" />.
/// </remarks>
internal static class UiEditorEngine
{
    /// <summary>The path the build copies the engine to, under the app's web root.</summary>
    public const string Path = "/js/rask-ui-editor.js";

    /// <summary>The first eight hex digits of the bundle's SHA-256: the URL changes when the bytes do.</summary>
    public const string Version = "37164256";

    /// <summary>The engine's URL under <paramref name="pathBase" />, carrying its version.</summary>
    public static string Href(string? pathBase = null) => (pathBase ?? string.Empty) + Path + "?v=" + Version;
}
