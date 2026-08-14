using Rask.ObjectStore;

namespace Rask.Example.Crdt.Devices;

/// <summary>
///     The shared bucket, with a switch. Every call fails while the device is "offline", exactly as a
///     real client would see it, so the demo exercises the real offline path rather than a special case
///     the engine knows about.
/// </summary>
public sealed class SwitchableObjectStore(IObjectStore inner) : IObjectStore
{
    /// <summary>Whether this device can currently reach the bucket.</summary>
    public bool Online { get; set; } = true;

    public Task<byte[]?> GetRangeAsync(string key, long offset, int count, CancellationToken ct = default)
    {
        Check();
        return inner.GetRangeAsync(key, offset, count, ct);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        Check();
        return inner.OpenReadAsync(key, ct);
    }

    public Task PutAsync(string key, byte[] content, CancellationToken ct = default)
    {
        Check();
        return inner.PutAsync(key, content, ct);
    }

    public Task PutAsync(string key, Stream content, long length, CancellationToken ct = default)
    {
        Check();
        return inner.PutAsync(key, content, length, ct);
    }

    public Task<bool> TryCreateAsync(string key, byte[] content, CancellationToken ct = default)
    {
        Check();
        return inner.TryCreateAsync(key, content, ct);
    }

    public Task<IReadOnlyList<ObjectEntry>> ListAsync(
        string prefix, string? startAfter = null, CancellationToken ct = default)
    {
        Check();
        return inner.ListAsync(prefix, startAfter, ct);
    }

    public Task<IReadOnlyList<string>> ListPrefixesAsync(string prefix, CancellationToken ct = default)
    {
        Check();
        return inner.ListPrefixesAsync(prefix, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Check();
        return inner.DeleteAsync(key, ct);
    }

    private void Check()
    {
        if (!Online)
        {
            throw new HttpRequestException("this device is offline");
        }
    }
}
