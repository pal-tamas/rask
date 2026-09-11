namespace Rask.DevTools.Endpoints;

/// <summary>
///     Whether the component kit the panel is drawn with is in the app.
/// </summary>
/// <remarks>
///     Rask.DevTools references Rask.Ui privately: the package declares no dependency on it, so a Release publish of an
///     app that never referenced Rask.Ui carries none of it. The app brings its own copy — every <c>rask new</c> template
///     and the <c>Rask</c> meta-package do — and without one the panel cannot render, so the devtools stay off.
/// </remarks>
internal static class DevToolsUiKit
{
    /// <summary>
    ///     A kit type, named rather than referenced, so asking never loads an assembly that is not there. A test pins it to
    ///     the real type, so a rename in Rask.Ui fails there rather than turning the devtools off in every app.
    /// </summary>
    internal const string ProbeTypeName = "Rask.Ui.UiStylesheet, Rask.Ui";

    /// <summary>Whether the kit resolves in this process.</summary>
    internal static bool IsAvailable()
    {
        try
        {
            return Type.GetType(ProbeTypeName, throwOnError: false) is not null;
        }
        catch (Exception ex) when (ex is FileLoadException or BadImageFormatException)
        {
            // Present but unloadable is as good as absent for drawing a panel.
            return false;
        }
    }
}
