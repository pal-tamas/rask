using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Rask.Wire;

namespace Rask.Cqrs.Server;

/// <summary>
///     Holds the parts of a chunked upload until the message that carries them arrives.
/// </summary>
/// <remarks>
///     <para>
///         A browser's <c>fetch</c> reads a request body into memory before sending it, so a single-shot
///         upload costs its own size in the tab — the reason a large file has to arrive in pieces. Each
///         piece is appended to a file on disk here rather than held in memory, so a server assembling a
///         500 MB upload for a hundred users at once is bounded by disk, not by RAM.
///     </para>
///     <para>
///         Sessions are keyed by the caller as well as the id. An upload id is a bearer of nothing — the
///         caller who opened the session is the only one who may append to it or spend it, so a guessed
///         id cannot be used to inject bytes into somebody else's message, or to read what they sent.
///     </para>
/// </remarks>
internal sealed class UploadSessionStore : IDisposable
{
    private readonly ConcurrentDictionary<string, UploadSession> _sessions = new(StringComparer.Ordinal);

    // What each caller holds open. One upload is bounded by MaxUploadBytes, but a caller looping over
    // fresh upload ids is bounded only by this — so every open, close and written byte is counted here,
    // under one lock, where the two limits can be read and moved together.
    private readonly Dictionary<string, OwnerUsage> _owners = new(StringComparer.Ordinal);
    private readonly Lock _quota = new();
    private readonly string _root;
    private readonly TimeProvider _time;

    public UploadSessionStore(CqrsServerOptions options, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        _time = time ?? TimeProvider.System;
        _root = Path.Combine(Path.GetTempPath(), "rask-cqrs-uploads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>Appends <paramref name="body" /> to one file of a session, creating the session if new.</summary>
    /// <returns>The number of bytes the server now holds for that file.</returns>
    public async Task<long> AppendAsync(
        string owner,
        string uploadId,
        int fileIndex,
        long offset,
        string name,
        string? contentType,
        Stream body,
        CqrsServerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentException.ThrowIfNullOrEmpty(uploadId);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(options);

        if (fileIndex < 0 || fileIndex >= options.MaxFileCount)
        {
            throw new BadRequestException(
                StatusCodes.Status400BadRequest, "Malformed upload",
                $"A file index must be between 0 and {options.MaxFileCount - 1}.");
        }

        Prune(options);

        var session = Open(owner, uploadId, options);
        var part = session.Part(fileIndex, _root);

        // Kept from the first chunk. A later chunk claiming a different name would be a sender changing
        // its mind mid-file, and the name the bytes started under is the honest one.
        part.Name ??= name;
        part.ContentType ??= contentType;

        // One writer per part. Two concurrent chunks for the same file would interleave into a corrupt
        // assembly, and the offset check below could not tell that had happened.
        await part.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The offset the client believes it is at must be the length the server actually holds.
            // Without this a retried chunk would append twice and silently corrupt the file, which is the
            // failure mode a resumable upload exists to avoid.
            if (offset != part.Length)
            {
                throw new UploadOffsetException(part.Length);
            }

            await WriteAsync(session, part, body, options, cancellationToken).ConfigureAwait(false);

            session.Touched = _time.GetUtcNow();
            return part.Length;
        }
        finally
        {
            part.Gate.Release();
        }
    }

    // The caller's session for this id, opened if it is new and the caller has room for another.
    private UploadSession Open(string owner, string uploadId, CqrsServerOptions options)
    {
        var key = Key(owner, uploadId);

        lock (_quota)
        {
            if (_sessions.TryGetValue(key, out var open))
            {
                return open;
            }

            if (!_owners.TryGetValue(owner, out var usage))
            {
                _owners[owner] = usage = new OwnerUsage(owner);
            }

            if (usage.Sessions >= options.MaxOpenUploads)
            {
                throw new BadRequestException(
                    StatusCodes.Status429TooManyRequests, "Too many open uploads",
                    $"A caller may have {options.MaxOpenUploads} uploads open at once. Send the message one "
                    + "belongs to, or wait for an abandoned one to lapse.");
            }

            usage.Sessions++;
            return _sessions[key] = new UploadSession(usage, _time.GetUtcNow());
        }
    }

    // Counts bytes BEFORE they are written, so both caps bound what reaches the disk rather than
    // reporting afterwards on something already there.
    private void Reserve(UploadSession session, int bytes, CqrsServerOptions options)
    {
        lock (_quota)
        {
            if (session.Closed)
            {
                throw new BadRequestException(
                    StatusCodes.Status400BadRequest, "Unknown upload", "The upload was already spent.");
            }

            if (session.Total + bytes > options.MaxUploadBytes)
            {
                throw new BadRequestException(
                    StatusCodes.Status413PayloadTooLarge, "Upload too large",
                    $"The upload exceeds the {options.MaxUploadBytes} byte limit.");
            }

            if (session.Owner.Bytes + bytes > options.MaxOpenUploadBytes)
            {
                throw new BadRequestException(
                    StatusCodes.Status429TooManyRequests, "Too much uploaded and not sent",
                    $"A caller's open uploads may hold {options.MaxOpenUploadBytes} bytes between them. Send "
                    + "the message one belongs to, or wait for an abandoned one to lapse.");
            }

            session.Total += bytes;
            session.Owner.Bytes += bytes;
        }
    }

    // Gives a removed session's share back to its caller.
    private void Close(UploadSession session)
    {
        lock (_quota)
        {
            var usage = session.Owner;
            session.Closed = true;
            usage.Sessions--;
            usage.Bytes -= session.Total;

            if (usage.Sessions == 0)
            {
                _owners.Remove(usage.Name);
            }
        }
    }

    private async Task WriteAsync(
        UploadSession session,
        UploadPart part,
        Stream body,
        CqrsServerOptions options,
        CancellationToken cancellationToken)
    {
        var file = new FileStream(
            part.Path, FileMode.Append, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await using (file.ConfigureAwait(false))
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                Reserve(session, read, options);

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                part.Length += read;
            }
        }
    }

    /// <summary>
    ///     Takes a session's parts and removes it, so an upload can back exactly one message.
    /// </summary>
    /// <remarks>
    ///     Single-use deliberately: an id that stayed valid could replay somebody's upload into a second
    ///     message. The files outlive the entry — the returned <see cref="RemoteFile" />s own them, and
    ///     they are deleted when the request that spends them is done.
    /// </remarks>
    public IReadOnlyList<RemoteFile>? Take(string owner, string uploadId)
    {
        if (!_sessions.TryRemove(Key(owner, uploadId), out var session))
        {
            return null;
        }

        Close(session);

        // Contiguous from zero, or the message's indices do not line up with what arrived. A gap would
        // hand a handler the wrong file, which is the failure this pairing exists to prevent.
        var count = session.Parts.Count;
        var files = new List<RemoteFile>(count);
        for (var i = 0; i < count; i++)
        {
            if (!session.Parts.TryGetValue(i, out var part))
            {
                session.Delete();
                return null;
            }

            var path = part.Path;
            files.Add(RemoteFile.FromStream(
                part.Name ?? "file",
                part.ContentType,
                part.Length,
                _ => new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.DeleteOnClose)));
        }

        return files;
    }

    /// <summary>Drops sessions that were opened and never spent, so an abandoned upload is not a leak.</summary>
    private void Prune(CqrsServerOptions options)
    {
        var cutoff = _time.GetUtcNow() - options.UploadSessionLifetime;
        foreach (var (key, session) in _sessions)
        {
            if (session.Touched < cutoff && _sessions.TryRemove(key, out var dropped))
            {
                Close(dropped);
                dropped.Delete();
            }
        }
    }

    // The caller is part of the key, so an upload id alone opens nothing.
    private static string Key(string owner, string uploadId) => owner + "\0" + uploadId;

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Delete();
        }

        _sessions.Clear();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone: nothing to do.
        }
        catch (IOException)
        {
            // A part still open elsewhere. The OS reclaims the temp directory; failing shutdown over it
            // would be worse than leaving it.
        }
    }

    /// <summary>A new, unguessable upload id.</summary>
    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // Read and written under _quota only.
    private sealed class OwnerUsage(string name)
    {
        public string Name { get; } = name;

        public int Sessions { get; set; }

        public long Bytes { get; set; }
    }

    private sealed class UploadSession(OwnerUsage owner, DateTimeOffset opened)
    {
        public ConcurrentDictionary<int, UploadPart> Parts { get; } = new();

        public OwnerUsage Owner { get; } = owner;

        public DateTimeOffset Touched { get; set; } = opened;

        // Total and Closed are read and written under _quota only: two parts of one upload write at once.
        public long Total { get; set; }

        public bool Closed { get; set; }

        public UploadPart Part(int index, string root) =>
            Parts.GetOrAdd(index, i => new UploadPart(
                Path.Combine(root, Guid.NewGuid().ToString("N") + "-" + i.ToString(CultureInfo.InvariantCulture))));

        public void Delete()
        {
            foreach (var part in Parts.Values)
            {
                part.Delete();
            }

            Parts.Clear();
        }
    }

    private sealed class UploadPart(string path)
    {
        public string Path { get; } = path;

        public long Length { get; set; }

        public string? Name { get; set; }

        public string? ContentType { get; set; }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void Delete()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // Being read by the request that spent it; DeleteOnClose finishes the job.
            }
        }
    }
}
