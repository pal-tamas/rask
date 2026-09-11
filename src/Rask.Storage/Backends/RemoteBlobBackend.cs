using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace Rask.Storage.Backends;

/// <summary>What the S3 and Azure stores share: the temp spool, reading bodies, reading listings, and errors.</summary>
internal abstract class RemoteBlobBackend(HttpClient http, TimeProvider time) : IBlobBackend
{
    private const int MaxErrorBodyBytes = 64 * 1024;
    private const int MaxListingBytes = 16 * 1024 * 1024;

    private readonly Lock _spoolGate = new();
    private string? _spoolDirectory;

    private static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    protected HttpClient Http { get; } = http;

    protected TimeProvider Time { get; } = time;

    public abstract StorageProvider Provider { get; }

    public abstract long MaxSinglePutBytes { get; }

    /// <summary>
    /// A spool file in a directory private to this backend. Not a fixed <c>/tmp</c> path: a predictable shared folder
    /// can be created first by another local user, who then owns it and could swap a vetted upload's bytes between
    /// the sniff and the upload. <see cref="Directory.CreateTempSubdirectory"/> makes a fresh one, owner-only on Unix.
    /// </summary>
    public string CreateSpoolPath()
    {
        lock (_spoolGate)
        {
            if (_spoolDirectory is null || !Directory.Exists(_spoolDirectory))
            {
                _spoolDirectory = Directory.CreateTempSubdirectory("rask-storage-").FullName;
            }

            return Path.Combine(_spoolDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        }
    }

    public abstract Task PutFileAsync(string key, string sourcePath, long length, BlobHeaders headers,
        CancellationToken cancellationToken);

    public abstract Task<Stream?> OpenReadAsync(string key, long offset, long? count, CancellationToken cancellationToken);

    public Task<Stream?> OpenForServingAsync(string key, long size, long? rangeFrom, long? rangeTo,
        CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(new BlobRangeStream(this, key, size, rangeFrom, rangeTo));

    public abstract Task DeleteAsync(string key, CancellationToken cancellationToken);

    public abstract IAsyncEnumerable<BlobEntry> ListAsync(string prefix, CancellationToken cancellationToken);

    public abstract bool TryPresign(string key, TimeSpan lifetime, string contentType, string contentDisposition,
        [NotNullWhen(true)] out string? url);

    public Task DeleteStaleSpoolAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        if (_spoolDirectory is { } directory)
        {
            SpoolCleanup.DeleteOlderThan(directory, olderThan, cancellationToken);
        }

        return Task.CompletedTask;
    }

    protected static FileStream OpenSpool(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, bufferSize: 0,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    protected static void RequireKey(string key)
    {
        if (!KeyLayout.IsValid(key))
        {
            throw new ArgumentException($"'{key}' is not a storage key.", nameof(key));
        }
    }

    protected static void RequirePrefix(string prefix)
    {
        if (!KeyLayout.IsValidPrefix(prefix))
        {
            throw new ArgumentException($"'{prefix}' is not a storage key prefix.", nameof(prefix));
        }
    }

    /// <summary>Turns a GET response into the caller's stream: null when missing, empty past the end.</summary>
    protected async Task<Stream?> ReadBodyAsync(HttpResponseMessage response, long offset, CancellationToken cancellationToken)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                response.Dispose();
                return null;
            case HttpStatusCode.RequestedRangeNotSatisfiable:
                response.Dispose();
                return Stream.Null;
            case HttpStatusCode.OK when offset > 0:
                response.Dispose();
                throw new IOException($"{Provider} ignored the requested range and returned the whole object.");
        }

        await EnsureSuccessAsync(response, "read an object", cancellationToken).ConfigureAwait(false);
        try
        {
            var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new ResponseStream(response, body);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Throws for a failed response with the service's error code and nothing else from the body: an S3
    /// signature error echoes the access key id and the canonical request, which have no business in a log.
    /// </summary>
    protected async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = response.StatusCode;
        var code = await ErrorCodeAsync(response, cancellationToken).ConfigureAwait(false);
        var reason = response.ReasonPhrase;
        response.Dispose();

        var hint = code switch
        {
            "RequestTimeTooSkewed" =>
                " This server's clock is more than 15 minutes away from the storage service's; fix the clock (NTP).",
            "SignatureDoesNotMatch" or "InvalidAccessKeyId" or "AuthenticationFailed" or "AuthorizationFailure" =>
                " Check the Storage__ credentials for this provider.",
            "NoSuchBucket" or "ContainerNotFound" => " The bucket or container does not exist; create it first.",
            _ => "",
        };

        throw new HttpRequestException(
            $"{Provider} refused to {operation}: {(int)status} {code ?? reason}.{hint}", null, status);
    }

    protected static async Task<XElement> ReadXmlAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bytes = await ReadCappedAsync(response, MaxListingBytes, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The storage service's listing was larger than 16 MB.");
        using var buffer = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(buffer, SafeXml);
        return XElement.Load(reader);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Headers.TryGetValues("x-ms-error-code", out var values))
        {
            return Plain(values.FirstOrDefault());
        }

        try
        {
            var bytes = await ReadCappedAsync(response, MaxErrorBodyBytes, cancellationToken).ConfigureAwait(false);
            if (bytes is not { Length: > 0 })
            {
                return null;
            }

            using var buffer = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(buffer, SafeXml);
            return Plain(XElement.Load(reader).Element("Code")?.Value);
        }
        catch (Exception ex) when (ex is XmlException or IOException or HttpRequestException)
        {
            return null;
        }
    }

    // An error code is a short identifier; anything else is not repeated.
    private static string? Plain(string? code) =>
        code is { Length: > 0 and <= 64 } && code.All(char.IsAsciiLetterOrDigit) ? code : null;

    private static async Task<byte[]?> ReadCappedAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > limit)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
    }

    /// <summary>A response body that releases its response when the caller disposes it.</summary>
    private sealed class ResponseStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

/// <summary>
/// A seekable, lazily opened stream over a remote object, so ASP.NET's range and conditional handling works on a
/// file that lives in a bucket.
/// </summary>
/// <remarks>
/// Nothing is fetched until the first read, so a <c>HEAD</c> or a <c>304</c> never costs a request to the store.
/// A read opens the object at the current position — for exactly the requested range when the request asked
/// for one — and a seek elsewhere closes it, so the next read reopens where it is needed.
/// </remarks>
internal sealed class BlobRangeStream(IBlobBackend backend, string key, long length, long? rangeFrom, long? rangeTo) : Stream
{
    private Stream? _inner;
    private bool _innerIsLimited;
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(offset));
        if (target != _position)
        {
            CloseInner();
            _position = target;
        }

        return _position;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty || _position >= length)
        {
            return 0;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (_inner is null)
            {
                long? count = rangeFrom == _position && rangeTo is { } to && to >= _position ? to - _position + 1 : null;
                _inner = await backend.OpenReadAsync(key, _position, count, cancellationToken).ConfigureAwait(false)
                         ?? throw new IOException("The stored file's bytes are missing from the store.");
                _innerIsLimited = count is not null;
            }

            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                _position += read;
                return read;
            }

            // The requested range is exhausted but the caller reads on: reopen once, to the end.
            if (!_innerIsLimited)
            {
                return 0;
            }

            CloseInner();
            rangeFrom = null;
        }

        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseInner();
        }

        base.Dispose(disposing);
    }

    private void CloseInner()
    {
        _inner?.Dispose();
        _inner = null;
    }
}
