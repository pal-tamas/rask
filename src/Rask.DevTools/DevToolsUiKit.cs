using Rask.Core.Diagnostics;

namespace Rask.DevTools;

/// <summary>
///     Whether the component kit the panel is drawn with is in the app.
/// </summary>
/// <remarks>
///     Rask.DevTools references Rask.Ui privately: the package declares no dependency on it, so a Release publish of an
///     app that never referenced Rask.Ui carries none of it. The app brings its own copy — every <c>rask new</c> template
///     and the <c>Rask</c> meta-package do — and without one the panel cannot render, so the devtools stay off on either
///     host.
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

    /// <summary>Says once, at startup, why the devtools are off — instead of a pill that opens a broken frame.</summary>
    internal static void ReportMissing() =>
        RaskDiagnostics.Report(
            RaskLogLevel.Warning,
            "Rask.DevTools",
            "Rask DevTools are off: their panel is drawn with Rask.Ui, which this app does not reference. Add a "
            + "PackageReference to Rask.Ui to turn them on — apps created with `rask new` and the Rask package already "
            + "have one.");
}
