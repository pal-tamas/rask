using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Rask.Cqrs.Client;

/// <summary>
///     Sends a message to the server over HTTP and decodes the answer. Registered by
///     <c>AddRaskCqrsClient</c>; reached only through <see cref="IDispatcher" />.
/// </summary>
/// <remarks>
///     The verb comes from the message's own shape: a query is safe and idempotent, so it travels as a
///     GET and can be cached; anything that mutates travels as a POST. A query too long for a url, or
///     one carrying files, falls back to POST — the result is identical, and the fallback exists because
///     a url ceiling differs per proxy and a query that only fails in production is the worst way to
///     find that out.
/// </remarks>
internal sealed class RemoteDispatch(HttpClient http, RaskCqrsClientOptions options) : IRemoteDispatch
{
    public async Task<TResult> SendAsync<TResult>(
        RemoteContract contract,
        object message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(message);

        var response = await SendCoreAsync(contract, message, cancellationToken).ConfigureAwait(false);

        if (contract.ReturnsFile)
        {
            // The response is the file. It must not be disposed here — the caller reads the body — so
            // ownership passes to the FileDownload, which disposes the response when its stream closes.
            return (TResult)(object)await ReadFileAsync(response, contract, cancellationToken).ConfigureAwait(false);
        }

        using (response)
        {
            if (contract.ReadResult is null)
            {
                return default!;
            }

            var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return (TResult)Decode(contract, payload)!;
        }
    }

    public async Task SendAsync(RemoteContract contract, object message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(message);

        using var response = await SendCoreAsync(contract, message, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(
        RemoteContract contract,
        object notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(notification);

        using var response = await SendCoreAsync(contract, notification, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        RemoteContract contract,
        object message,
        CancellationToken cancellationToken)
    {
        var files = new List<RemoteFile>();
        var json = Encode(contract, message, files);

        // Anything large goes up in bounded pieces BEFORE the message does. A browser's fetch reads a
        // request body into memory before sending it, so a single-shot upload of a 500 MB file costs
        // 500 MB in the tab — the file is read in slices either way (that is what RaskFile does on every
        // host), but only chunking keeps the REQUEST small too.
        var uploadId = await UploadLargeFilesAsync(files, cancellationToken).ConfigureAwait(false);

        using var request = Build(contract, json, files, uploadId);
        if (uploadId is not null)
        {
            request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadHeader, uploadId);
        }

        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.RequestHeader,
            RemoteEndpointDefaults.RequestHeaderValue);

        if (options.ConfigureRequestAsync is { } configure)
        {
            await configure(request, cancellationToken).ConfigureAwait(false);
        }

        HttpResponseMessage response;
        try
        {
            // ResponseHeadersRead so a streamed download is not buffered into memory before the caller
            // ever sees it — the difference between a constant-memory export and one that isn't.
            response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                   !cancellationToken.IsCancellationRequested)
        {
            // No status: the request never got an answer. That null is the signal, and the cause is the
            // inner exception. A cancellation the caller asked for is not this — it propagates.
            throw new RemoteDispatchException(
                $"'{contract.Name}' could not reach the server.", ex)
            {
                MessageName = contract.Name,
            };
        }

        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                throw await FailureAsync(contract, response, cancellationToken).ConfigureAwait(false);
            }
        }

        return response;
    }

    private HttpRequestMessage Build(
        RemoteContract contract,
        byte[] json,
        List<RemoteFile> files,
        string? uploadId = null)
    {
        // PathBase first: a sub-path deploy (a WASM bundle served under /myapp/) reaches its own host
        // only through that prefix, and the server maps the endpoint pair under the same one. Without it
        // the request leaves for the site root and 404s — visible only once someone deploys under a path.
        var path = Rask.Core.Live.LiveOptions.PathBase + options.RoutePrefix
                   + "/" + Uri.EscapeDataString(contract.Name);

        // With an upload session the bytes are already on the server, so the message travels as plain
        // JSON and carries only the session id. Without one, the files ride along as multipart.
        if (files.Count > 0 && uploadId is null)
        {
            return new HttpRequestMessage(HttpMethod.Post, path) { Content = Multipart(json, files) };
        }

        if (uploadId is not null)
        {
            return new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new ByteArrayContent(json)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
                },
            };
        }

        if (contract.Kind == RemoteMessageKind.Query)
        {
            var url = path + "?" + RemoteEndpointDefaults.MessageQueryParameter + "="
                      + Uri.EscapeDataString(Encoding.UTF8.GetString(json));

            if (url.Length <= options.MaxQueryUrlLength)
            {
                return new HttpRequestMessage(HttpMethod.Get, url);
            }
        }

        return new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(json)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };
    }

    /// <summary>
    ///     Sends every file in bounded chunks and returns the session id the message will spend, or null
    ///     when nothing is large enough to be worth it.
    /// </summary>
    /// <remarks>
    ///     All-or-nothing per message: once one file needs chunking they all go that way, because the
    ///     server resolves a message's files from ONE source. Mixing would mean pairing half the indices
    ///     against a multipart body and half against a session, which is a way to hand a handler the wrong
    ///     file — the failure this transport already goes out of its way to make impossible.
    /// </remarks>
    private async Task<string?> UploadLargeFilesAsync(
        List<RemoteFile> files,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0 || !files.Any(f => f.Size < 0 || f.Size > options.ChunkedUploadThreshold))
        {
            return null;
        }

        var uploadId = Guid.NewGuid().ToString("N");
        var buffer = new byte[options.UploadChunkSize];

        for (var index = 0; index < files.Count; index++)
        {
            // Opened once and read forward. A RaskFile reads in slices on every host — Blob.slice in the
            // browser, a FileStream on the server — so the file is never materialised whole on either
            // side of this loop.
            await using var source = files[index].OpenReadStream(cancellationToken);

            long offset = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await SendChunkAsync(uploadId, index, offset, files[index], buffer, read, cancellationToken)
                    .ConfigureAwait(false);
                offset += read;
            }
        }

        return uploadId;
    }

    private async Task SendChunkAsync(
        string uploadId,
        int index,
        long offset,
        RemoteFile file,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var path = Rask.Core.Live.LiveOptions.PathBase + options.RoutePrefix
                   + "/" + RemoteEndpointDefaults.UploadSegment;

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            // A copy, because the buffer is reused for the next chunk while this content is still owned
            // by the request — and because a retry has to be able to send the same bytes again.
            Content = new ByteArrayContent(buffer, 0, count),
        };

        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.RequestHeader, RemoteEndpointDefaults.RequestHeaderValue);
        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.UploadHeader, uploadId);
        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.UploadFileHeader, index.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.UploadOffsetHeader, offset.ToString(CultureInfo.InvariantCulture));

        // Url-encoded: a filename is user input, and a raw one can carry CR/LF or non-ASCII, neither of
        // which belongs in a header value.
        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.UploadNameHeader, Uri.EscapeDataString(file.Name));
        request.Headers.TryAddWithoutValidation(
            RemoteEndpointDefaults.UploadTypeHeader, Uri.EscapeDataString(file.ContentType));

        if (options.ConfigureRequestAsync is { } configure)
        {
            await configure(request, cancellationToken).ConfigureAwait(false);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException &&
                                   !cancellationToken.IsCancellationRequested)
        {
            throw new RemoteDispatchException("The upload could not reach the server.", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            throw new RemoteDispatchException(
                $"The server refused a chunk of the upload ({(int)response.StatusCode}).")
            {
                StatusCode = (int)response.StatusCode,
            };
        }
    }

    private static MultipartFormDataContent Multipart(byte[] json, List<RemoteFile> files)
    {
        var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(json) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }, "message" },
        };

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var part = new StreamContent(file.OpenReadStream());
            part.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);

            // The part name is the index the JSON wrote, not the file's name: that is what pairs a part
            // back to the property it came from, and it is the one thing a client cannot get wrong.
            content.Add(part, i.ToString(System.Globalization.CultureInfo.InvariantCulture), file.Name);
        }

        return content;
    }

    private static byte[] Encode(RemoteContract contract, object message, List<RemoteFile> files)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            contract.WriteMessage(writer, message, files);
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static object? Decode(RemoteContract contract, byte[] payload)
    {
        if (payload.Length == 0)
        {
            return null;
        }

        var reader = new Utf8JsonReader(payload);
        reader.Read();
        return contract.ReadResult!(ref reader);
    }

    private static async Task<FileDownload> ReadFileAsync(
        HttpResponseMessage response,
        RemoteContract contract,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var disposition = response.Content.Headers.ContentDisposition;
        var name = Trim(disposition?.FileNameStar) ?? Trim(disposition?.FileName) ?? contract.Name;

        return FileDownload.FromStream(
            name,
            response.Content.Headers.ContentType?.MediaType,
            new ResponseStream(stream, response),
            response.Content.Headers.ContentLength);
    }

    // Content-Disposition filenames arrive quoted more often than not, and a quoted name reaches the
    // save dialog with the quotes in it.
    private static string? Trim(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Trim('"');

    private static async Task<RemoteDispatchException> FailureAsync(
        RemoteContract contract,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? type = null;
        string? title = null;
        string? detail = null;

        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                ReadProblem(payload, ref type, ref title, ref detail);
            }
            catch (JsonException)
            {
                // A malformed body is not worth losing the status code over.
            }
        }

        return new RemoteDispatchException(
            $"'{contract.Name}' failed on the server: {(int)response.StatusCode} {title ?? response.ReasonPhrase}.")
        {
            MessageName = contract.Name,
            StatusCode = (int)response.StatusCode,
            ProblemType = type,
            Detail = detail,
        };
    }

    // Hand-read rather than deserialized: this package does no reflection anywhere, and a problem
    // document is three strings.
    private static void ReadProblem(byte[] payload, ref string? type, ref string? title, ref string? detail)
    {
        var reader = new Utf8JsonReader(payload);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "type":
                    type = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                case "title":
                    title = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                case "detail":
                    detail = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    // Keeps the response alive for as long as the body is being read. Disposing an HttpResponseMessage
    // disposes its content stream, so a FileDownload handed the bare stream would fail the moment the
    // response went out of scope — for large files, part-way through the save.
    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

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
