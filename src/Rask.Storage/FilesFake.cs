using Microsoft.AspNetCore.Http;
using Rask.Batteries;

namespace Rask.Storage;

/// <summary>An in-memory file store that remembers what a test asked of it.</summary>
public sealed class FilesFake : IFiles, IDisposable
{
    private readonly Dictionary<Guid, Held> _held = [];
    private readonly List<SavedFile> _saved = [];
    private readonly List<Guid> _deleted = [];
    private readonly IFiles? _previous;
    private readonly Lock _gate = new();

    internal FilesFake()
    {
        _previous = Files.Faked.Value;
        Files.Faked.Value = this;
    }

    /// <summary>
    ///     Asks about what was saved: <c>files.Saved().Named("avatar.png").Once()</c>,
    ///     <c>files.Saved().Public().Once()</c>, <c>files.Saved().None()</c>. <c>Single()</c> hands back the
    ///     one that matched, to assert on its id or bytes.
    /// </summary>
    public Counting<SavedFile> Saved()
    {
        lock (_gate)
        {
            return new Counting<SavedFile>(
                [.. _saved], "file", "saved", static f => $"\"{f.Name}\" ({f.Size} bytes{(f.IsPublic ? ", public" : "")})");
        }
    }

    /// <summary>Asks about what was deleted: <c>files.Deleted(id).Once()</c>.</summary>
    public Counting<Guid> Deleted()
    {
        lock (_gate)
        {
            return new Counting<Guid>([.. _deleted], "file", "deleted", static id => id.ToString());
        }
    }

    /// <inheritdoc cref="Deleted()" />
    /// <param name="id">The file to ask about.</param>
    public Counting<Guid> Deleted(Guid id) => Deleted().Where(d => d == id, $"{id}");

    /// <summary>The bytes stored under <paramref name="id" />, or <c>null</c> when there are none.</summary>
    /// <param name="id">The file to read.</param>
    public byte[]? Bytes(Guid id)
    {
        lock (_gate)
        {
            return _held.TryGetValue(id, out var held) ? held.Content : null;
        }
    }

    /// <summary>Empties it and forgets what was asked of it, without putting the real store back.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _held.Clear();
            _saved.Clear();
            _deleted.Clear();
        }
    }

    /// <summary>Puts the real file store back.</summary>
    public void Dispose() => Files.Faked.Value = _previous;

    /// <inheritdoc />
    public async Task<StoredFile> Add(
        Func<long, CancellationToken, Stream> openRead, string name, long size, bool isPublic, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        var content = openRead(long.MaxValue, cancellationToken);
        await using (content.ConfigureAwait(false))
        {
            return await ((IFiles)this).Add(content, name, isPublic, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<StoredFile> Add(Stream content, string name, bool isPublic, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();
        var id = Guid.NewGuid();

        lock (_gate)
        {
            _held[id] = new Held(bytes, name, isPublic);
            _saved.Add(new SavedFile(id, name, bytes.LongLength, isPublic));
        }

        return Row(id, name, bytes.LongLength, isPublic);
    }

    /// <inheritdoc />
    public Task<StoredFile?> Get(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_held.TryGetValue(id, out var held)
                ? Row(id, held.Name, held.Content.LongLength, held.IsPublic)
                : null);
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenRead(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<Stream?>(
                _held.TryGetValue(id, out var held) ? new MemoryStream(held.Content, writable: false) : null);
        }
    }

    /// <inheritdoc />
    public Task<bool> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _deleted.Add(id);
            return Task.FromResult(_held.Remove(id));
        }
    }

    /// <inheritdoc />
    public string Url(Guid id) => $"/_rask/files/public/{id}";

    /// <inheritdoc />
    public Task<string?> SharedUrl(Guid id, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // Shaped like the real signed link, and carries the lifetime so a test can assert on it.
            return Task.FromResult<string?>(
                _held.ContainsKey(id) ? $"/_rask/files/{id}?expires={(long)lifetime.TotalSeconds}" : null);
        }
    }

    /// <inheritdoc />
    public IResult Download(Guid id) =>
        Bytes(id) is { } bytes ? Results.File(bytes, "application/octet-stream") : Results.NotFound();

    /// <summary>
    /// The row the real store would have written, built the same way it builds one. The content type is not
    /// sniffed and the digest is empty — a fake is not the place to reimplement either, and a test asserting
    /// on them would be testing the fake rather than the app.
    /// </summary>
    private static StoredFile Row(Guid id, string name, long size, bool isPublic) =>
        StoredFile.For(
            id,
            name,
            "application/octet-stream",
            size,
            sha256: "",
            StorageProvider.Disk,
            key: id.ToString("n"),
            isPublic,
            Clock.Now.UtcDateTime);

    private sealed record Held(byte[] Content, string Name, bool IsPublic);
}
