namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the File System Access API
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/File_System_API" />) — let the user
///     open a file from disk, edit it, and save it <em>back to the same file</em> (not just download a copy),
///     or work against a whole directory. Powers in-browser editors, note apps, and file managers. Inject it
///     through a component constructor.
/// </summary>
/// <remarks>
///     <para>
///         The picker must be opened from a <b>user-gesture handler</b>. The opaque browser handles can't
///         cross the interop boundary, so the framework holds each one JS-side under a minted id and hands
///         back an <see cref="IFileHandle" /> / <see cref="IDirectoryHandle" /> wrapper — <b>dispose</b> it
///         when done to release the JS-side reference. Works on <b>both transports</b>, but availability is
///         limited (Chromium-family; Firefox/Safari lack it) — gate on <see cref="IsSupportedAsync" /> and
///         fall back to <c>&lt;input type="file"&gt;</c> upload / a download where unsupported.
///     </para>
///     <para>
///         Cancelling a picker is not an error — the open/save methods return <c>null</c> (or an empty list)
///         rather than throwing.
///     </para>
/// </remarks>
public interface IFileSystemAccess
{
    /// <summary>Whether the browser supports the File System Access API (<c>"showOpenFilePicker" in window</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>
    ///     Shows the open-file picker for a single file and returns its handle, or <c>null</c> if cancelled.
    ///     Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<IFileHandle?> OpenFileAsync(FilePickerOptions? options = null);

    /// <summary>
    ///     Shows the open-file picker allowing multiple files and returns their handles (empty if cancelled).
    ///     Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<IReadOnlyList<IFileHandle>> OpenFilesAsync(FilePickerOptions? options = null);

    /// <summary>
    ///     Shows the save-file picker and returns a handle to write to, or <c>null</c> if cancelled. Must be
    ///     called from a user-gesture handler.
    /// </summary>
    ValueTask<IFileHandle?> SaveFileAsync(SaveFilePickerOptions? options = null);

    /// <summary>
    ///     Shows the directory picker and returns a handle to the chosen folder, or <c>null</c> if cancelled.
    ///     Must be called from a user-gesture handler.
    /// </summary>
    ValueTask<IDirectoryHandle?> OpenDirectoryAsync();
}
