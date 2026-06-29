using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>Tuning for an open/save file picker (the <c>types</c> filter of the File System Access API).</summary>
public sealed record FilePickerOptions
{
    /// <summary>Human label for the accepted file group (the <c>description</c> of the type filter).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>
    ///     Accepted types as MIME → extensions, e.g. <c>{ ["text/plain"] = [".txt", ".md"] }</c> (extensions
    ///     include the leading dot). Omitted lets the user pick any file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Accept { get; init; }
}

/// <summary>Tuning for a save file picker (adds a suggested name on top of <see cref="FilePickerOptions" />).</summary>
public sealed record SaveFilePickerOptions
{
    /// <summary>Pre-filled file name in the save dialog (the <c>suggestedName</c>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuggestedName { get; init; }

    /// <inheritdoc cref="FilePickerOptions.Description" />
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <inheritdoc cref="FilePickerOptions.Accept" />
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Accept { get; init; }
}

/// <summary>Wire shape for a picked file/entry handle — the JS-side id plus the entry name.</summary>
/// <param name="Id">The framework-minted id under which the live handle is held JS-side.</param>
/// <param name="Name">The file/entry name.</param>
public sealed record FileSystemHandleInfo(int Id, string Name);

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

/// <summary>A handle to one picked file. Dispose to release the JS-side reference.</summary>
public interface IFileHandle : IAsyncDisposable
{
    /// <summary>The file name (without path).</summary>
    string Name { get; }

    /// <summary>Reads the file's current contents as text (UTF-8).</summary>
    ValueTask<string> ReadTextAsync();

    /// <summary>Reads the file's current contents as bytes.</summary>
    ValueTask<byte[]> ReadBytesAsync();

    /// <summary>Overwrites the file with <paramref name="text" /> (UTF-8). Needs read-write permission.</summary>
    ValueTask WriteTextAsync(string text);

    /// <summary>Overwrites the file with <paramref name="bytes" />. Needs read-write permission.</summary>
    ValueTask WriteBytesAsync(byte[] bytes);
}

/// <summary>A handle to one picked directory. Dispose to release the JS-side reference.</summary>
public interface IDirectoryHandle : IAsyncDisposable
{
    /// <summary>The directory name.</summary>
    string Name { get; }

    /// <summary>Lists the names of the directory's immediate entries (files and sub-directories).</summary>
    ValueTask<IReadOnlyList<string>> ListAsync();

    /// <summary>
    ///     Returns a handle to the file <paramref name="name" /> in this directory, optionally creating it
    ///     when <paramref name="create" /> is <c>true</c>.
    /// </summary>
    ValueTask<IFileHandle> GetFileAsync(string name, bool create = false);
}

/// <summary>
///     Default <see cref="IFileSystemAccess" />, backed by the unified <see cref="IJSRuntime" />. The opaque
///     <c>FileSystemFileHandle</c> / <c>FileSystemDirectoryHandle</c> objects can't cross interop, so the
///     framework's <c>__raskFs</c> helper holds each under a minted id and exposes id-keyed read/write/list
///     operations; bytes ride the boundary base64-encoded.
/// </summary>
public sealed class FileSystemAccess(IJSRuntime js) : IFileSystemAccess
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskFs.isSupported");

    /// <inheritdoc />
    public async ValueTask<IFileHandle?> OpenFileAsync(FilePickerOptions? options = null)
    {
        var info = await js.InvokeAsync<FileSystemHandleInfo?>("__raskFs.openFile", options);
        return info is null ? null : new FileHandle(js, info);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IFileHandle>> OpenFilesAsync(FilePickerOptions? options = null)
    {
        var infos = await js.InvokeAsync<FileSystemHandleInfo[]>("__raskFs.openFiles", options);
        return infos is null ? [] : Array.ConvertAll(infos, info => (IFileHandle)new FileHandle(js, info));
    }

    /// <inheritdoc />
    public async ValueTask<IFileHandle?> SaveFileAsync(SaveFilePickerOptions? options = null)
    {
        var info = await js.InvokeAsync<FileSystemHandleInfo?>("__raskFs.saveFile", options);
        return info is null ? null : new FileHandle(js, info);
    }

    /// <inheritdoc />
    public async ValueTask<IDirectoryHandle?> OpenDirectoryAsync()
    {
        var info = await js.InvokeAsync<FileSystemHandleInfo?>("__raskFs.openDirectory");
        return info is null ? null : new DirectoryHandle(js, info);
    }

    private sealed class FileHandle(IJSRuntime js, FileSystemHandleInfo info) : IFileHandle
    {
        private bool _released;

        public string Name => info.Name;

        public ValueTask<string> ReadTextAsync() => js.InvokeAsync<string>("__raskFs.readText", info.Id);

        public async ValueTask<byte[]> ReadBytesAsync()
        {
            var base64 = await js.InvokeAsync<string>("__raskFs.readBytes", info.Id);
            return Convert.FromBase64String(base64);
        }

        public ValueTask WriteTextAsync(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return js.InvokeVoidAsync("__raskFs.writeText", info.Id, text);
        }

        public ValueTask WriteBytesAsync(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            return js.InvokeVoidAsync("__raskFs.writeBytes", info.Id, Convert.ToBase64String(bytes));
        }

        public async ValueTask DisposeAsync()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            await js.InvokeVoidAsync("__raskFs.release", info.Id);
        }
    }

    private sealed class DirectoryHandle(IJSRuntime js, FileSystemHandleInfo info) : IDirectoryHandle
    {
        private bool _released;

        public string Name => info.Name;

        public async ValueTask<IReadOnlyList<string>> ListAsync() =>
            await js.InvokeAsync<string[]>("__raskFs.list", info.Id);

        public async ValueTask<IFileHandle> GetFileAsync(string name, bool create = false)
        {
            ArgumentNullException.ThrowIfNull(name);
            var fileInfo = await js.InvokeAsync<FileSystemHandleInfo>("__raskFs.getFile", info.Id, name, create);
            return new FileHandle(js, fileInfo);
        }

        public async ValueTask DisposeAsync()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            await js.InvokeVoidAsync("__raskFs.release", info.Id);
        }
    }
}
