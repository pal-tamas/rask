using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
