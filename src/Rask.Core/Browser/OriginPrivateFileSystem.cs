using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>
///     Default <see cref="IOriginPrivateFileSystem" />, backed by the unified <see cref="IJSRuntime" />.
///     OPFS handles are opaque and cannot cross the interop boundary, and every operation needs the path
///     walked from the private root, so all access goes through the framework's <c>__raskOpfs</c> helper.
///     Bytes ride the boundary base64-encoded, as they do for <see cref="IFileSystemAccess" />.
/// </summary>
public sealed class OriginPrivateFileSystem(IJSRuntime js) : IOriginPrivateFileSystem
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskOpfs.isSupported");

    /// <inheritdoc />
    public ValueTask<bool> ExistsAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return js.InvokeAsync<bool>("__raskOpfs.exists", path);
    }

    /// <inheritdoc />
    public ValueTask<long?> GetSizeAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return js.InvokeAsync<long?>("__raskOpfs.size", path);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReadAsync(string path, long offset, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var base64 = await js.InvokeAsync<string?>("__raskOpfs.read", path, offset, count);
        return base64 is null ? null : Convert.FromBase64String(base64);
    }

    /// <inheritdoc />
    public ValueTask WriteAsync(string path, long offset, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentNullException.ThrowIfNull(bytes);

        return js.InvokeVoidAsync("__raskOpfs.write", path, offset, Convert.ToBase64String(bytes));
    }

    /// <inheritdoc />
    public ValueTask TruncateAsync(string path, long size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        return js.InvokeVoidAsync("__raskOpfs.truncate", path, size);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> ReadAllBytesAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var base64 = await js.InvokeAsync<string?>("__raskOpfs.readAll", path);
        return base64 is null ? null : Convert.FromBase64String(base64);
    }

    /// <inheritdoc />
    public ValueTask WriteAllBytesAsync(string path, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(bytes);

        return js.InvokeVoidAsync("__raskOpfs.writeAll", path, Convert.ToBase64String(bytes));
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(string path, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return js.InvokeVoidAsync("__raskOpfs.delete", path, recursive);
    }

    /// <inheritdoc />
    public ValueTask<string[]> ListAsync(string path = "")
    {
        ArgumentNullException.ThrowIfNull(path);
        return js.InvokeAsync<string[]>("__raskOpfs.list", path);
    }
}
