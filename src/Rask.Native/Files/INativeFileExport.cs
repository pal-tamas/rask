using Rask.Core.Diagnostics;

namespace Rask.Native.Files;

/// <summary>
///     How a file the app produced reaches the user on a native head. <c>Navigator.Download</c> means "here
///     are some bytes for you to keep"; in a browser that is a download, and on a device it is the OS share
///     sheet — the user picks Files, iCloud/Drive, mail, AirDrop, or another app.
/// </summary>
/// <remarks>
///     <para>
///         The framework has already written the bytes to a file under the app's cache directory by the time
///         this is called, so an implementation only has to present it: <c>UIActivityViewController</c> with
///         the file URL on iOS, <c>Intent.ACTION_SEND</c> with a <c>FileProvider</c> URI on Android. A
///         platform module registers its own through <see cref="INativePlatform.Register" />.
///     </para>
///     <para>
///         Deliberately not routed through <c>IShare</c>: that contract is Web-Share-shaped
///         (<c>ShareData</c> carries title/text/URL and no file), and widening it would change the meaning of
///         the same type on the WASM host.
///     </para>
/// </remarks>
public interface INativeFileExport
{
    /// <summary>Hand <paramref name="file" /> to the platform for the user to keep or send on.</summary>
    ValueTask ExportAsync(NativeFileExport file);
}

/// <summary>A file the app produced, staged on disk and ready to hand to the platform.</summary>
/// <param name="FileName">
///     The name to present to the user. Already sanitized to a single path segment — never a path.
/// </param>
/// <param name="ContentType">The MIME type, for the platform's app picker.</param>
/// <param name="Path">Absolute path to the staged file under the app's cache directory.</param>
public sealed record NativeFileExport(string FileName, string ContentType, string Path);

/// <summary>
///     The fallback <see cref="INativeFileExport" />, wired by <c>NativeAppHost</c> with <c>TryAdd</c> when
///     no platform module supplied one. The file is already staged on disk; with no platform UI to present it
///     the only honest thing left is to say where it went.
/// </summary>
/// <remarks>
///     This is what the plain <c>net10.0</c> head (CI, unit tests, a desktop harness) gets. It reports rather
///     than throws so a shared component calling <c>Navigator.Download</c> behaves the same everywhere — the
///     app does not crash on the one head that has no share sheet — while still leaving a trace that the
///     download had nowhere to go.
/// </remarks>
internal sealed class DiagnosticFileExport : INativeFileExport
{
    public ValueTask ExportAsync(NativeFileExport file)
    {
        ArgumentNullException.ThrowIfNull(file);
        RaskDiagnostics.Report(RaskLogLevel.Information, "Rask.Native",
            $"[Rask.Native] Download '{file.FileName}' was staged at {file.Path}, but no INativeFileExport is "
            + "registered, so it was not presented to the user. The iOS and Android platform modules register "
            + "one (the OS share sheet); register your own on host.Services to control where downloads go.");
        return default;
    }
}
