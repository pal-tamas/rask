using System.Globalization;

namespace Rask.Native.Files;

/// <summary>
///     Writes a staged download to disk so <see cref="INativeFileExport" /> has a file URL to hand the
///     platform, and reduces the caller's suggested name to something safe to use as one.
/// </summary>
/// <remarks>
///     The name matters here in a way it does not on the browser hosts. There, <c>Navigator.Download</c>'s
///     filename is only a suggestion the browser sanitizes before it touches a filesystem. On a native head it
///     becomes a real path, and the value can be attacker-influenced — an export named after a user-supplied
///     record title, a filename echoed back from an API. So it is reduced to a single path segment before it
///     is joined to anything.
/// </remarks>
internal static class NativeDownloadStaging
{
    private const string FallbackName = "download";

    /// <summary>
    ///     Writes <paramref name="bytes" /> under the app's cache directory and returns the export descriptor.
    ///     Each download gets its own subdirectory, so two files with the same name never collide and the
    ///     sanitized name can be presented to the user verbatim.
    /// </summary>
    public static async Task<NativeFileExport> StageAsync(
        string filename, string? contentType, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var safeName = SafeFileName(filename);
        var directory = Path.Combine(
            Path.GetTempPath(), "rask-downloads", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, safeName);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);

        return new NativeFileExport(safeName, contentType ?? "application/octet-stream", path);
    }

    /// <summary>
    ///     Reduces a caller-supplied download name to a single, safe path segment: no directory components on
    ///     either platform's separator, no traversal, no control characters, and never empty.
    /// </summary>
    internal static string SafeFileName(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return FallbackName;
        }

        // Both separators, unconditionally. Path.GetFileName is platform-aware, so on Unix it treats a
        // backslash as an ordinary character and would hand back "..\\..\\etc\\passwd" intact — a name that is
        // harmless on the host running the test and a traversal on a head that later joins it under Windows
        // rules. Cutting at the last of either is what makes the result platform-independent.
        var lastSeparator = filename.AsSpan().LastIndexOfAny('/', '\\');
        var candidate = lastSeparator >= 0 ? filename[(lastSeparator + 1)..] : filename;

        // Control characters (a newline in a name would also break the platform's own presentation) and
        // anything the filesystem rejects.
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Create(candidate.Length, candidate, (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c;
            }
        });

        cleaned = cleaned.Trim();
        // "." and ".." survive every filter above — they contain no invalid character and no separator — yet
        // neither is a file. Trailing dots and spaces are also stripped because Windows silently drops them,
        // which would turn "report.txt." into a different name than the one reported to the user.
        cleaned = cleaned.TrimEnd('.', ' ');

        return cleaned.Length == 0 ? FallbackName : cleaned;
    }
}
