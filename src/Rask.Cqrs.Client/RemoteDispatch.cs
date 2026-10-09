using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Rask.Client.Shared;
using Rask.Wire;

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
internal sealed class RemoteDispatch(
    HttpClient http,
    CqrsClientOptions options,
    IRemoteRequestValidator? validator = null) : IRemoteDispatch, IRemoteSubscriptions
{
    // Asks the browser's fetch to hand the body over as it arrives. Without it a WebAssembly HttpClient buffers the
    // whole response — and an event stream's whole response arrives when the subscription ends.
    private static readonly HttpRequestOptionsKey<bool> StreamingResponse = new("WebAssemblyEnableStreamingResponse");

    public IAsyncEnumerable<IEvent> Subscribe(
        RemoteContract contract,
        object? subscription,
        Action? connected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return Stream(contract, subscription, connected, cancellationToken);
    }

    private async IAsyncEnumerable<IEvent> Stream(
        RemoteContract contract,
        object? subscription,
        Action? connected,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The subscription record travels exactly as a query's message does: its generated JSON, url-encoded.
        var path = Endpoint($"{RemoteEndpointDefaults.EventsSegment}/{Uri.EscapeDataString(contract.Name)}")
                   + (subscription is null
                       ? string.Empty
                       : "?" + RemoteEndpointDefaults.MessageQueryParameter + "="
                         + Uri.EscapeDataString(
                             Encoding.UTF8.GetString(EventWire.EncodeMessage(contract, subscription))));

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation(RemoteEndpointDefaults.RequestHeader, RemoteEndpointDefaults.RequestHeaderValue);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Options.Set(StreamingResponse, true);
        if (options.ConfigureRequest is { } configure)
        {
            await configure(request, cancellationToken).ConfigureAwait(false);
        }

        // No per-attempt timeout here: the answer is meant to last as long as the page does. Reaching the server is
        // bounded by the caller's reconnect loop, which cancels this token when it gives up on it.
        using var response = await OpenAsync(contract, request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var disposeBody = body.ConfigureAwait(false);
        using var reader = new StreamReader(body, Encoding.UTF8);

        var data = new StringBuilder();
        string? name = null;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                       ?? throw new RemoteDispatchException($"The server closed the '{contract.Name}' subscription.")
                       {
                           MessageName = contract.Name,
                       };

            if (line.Length > 0)
            {
                ReadField(line, ref name, data);
                continue;
            }

            // A blank line ends an event.
            if (string.Equals(name, RemoteEndpointDefaults.ReadyEvent, StringComparison.Ordinal))
            {
                connected?.Invoke();
            }
            else if (data.Length > 0)
            {
                yield return EventWire.DecodeEvent(contract, Encoding.UTF8.GetBytes(data.ToString()));
            }

            name = null;
            data.Clear();
        }
    }

    // One line of a server-sent event: "field: value", or a ":" comment (the server's keep-alive).
    private static void ReadField(string line, ref string? name, StringBuilder data)
    {
        if (line[0] == ':')
        {
            return;
        }

        var colon = line.IndexOf(':', StringComparison.Ordinal);
        var field = colon < 0 ? line : line[..colon];
        var value = colon < 0 ? string.Empty : line[(colon + 1)..];
        if (value.StartsWith(' '))
        {
            value = value[1..];
        }

        if (string.Equals(field, "event", StringComparison.Ordinal))
        {
            name = value;
        }
        else if (string.Equals(field, "data", StringComparison.Ordinal))
        {
            data.Append(value);
        }
    }

    private async Task<HttpResponseMessage> OpenAsync(
        RemoteContract contract,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RemoteDispatchException($"'{contract.Name}' could not reach the server.", ex)
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

    public async Task<TResult> Send<TResult>(
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

    public async Task Send(RemoteContract contract, object message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(message);

        using var response = await SendCoreAsync(contract, message, cancellationToken).ConfigureAwait(false);
    }

    public async Task Publish(
        RemoteContract contract,
        object e,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(e);

        using var response = await SendCoreAsync(contract, e, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        RemoteContract contract,
        object message,
        CancellationToken cancellationToken)
    {
        await ValidateAsync(contract, message).ConfigureAwait(false);

        var files = new List<RemoteFile>();
        var json = Encode(contract, message, files);

        // Anything large goes up in bounded pieces BEFORE the message does. A browser's fetch reads a
        // request body into memory before sending it, so a single-shot upload of a 500 MB file costs
        // 500 MB in the tab — the file is read in slices either way (that is what IRaskFile does on every
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

        if (options.ConfigureRequest is { } configure)
        {
            await configure(request, cancellationToken).ConfigureAwait(false);
        }

        return await SendAsync(contract, request, cancellationToken).ConfigureAwait(false);
    }

    // Before anything is encoded, uploaded or sent. An invalid command should cost the user a
    // message, not a round trip — and on a slow connection the round trip is the whole delay
    // between pressing the button and being told which field is wrong.
    //
    // This is a convenience, never a control. The server runs the same rules again through
    // ValidationBehavior before any handler sees the request, so a caller that skips this (a
    // hand-written client, a replayed request) gains nothing by it.
    // Requests only. ValidationBehavior wraps the request pipeline, and Publish does not go
    // through it — so validating an event here would reject in the browser something the
    // server and every in-process publish accept, which is a worse failure than not checking.
    private async Task ValidateAsync(RemoteContract contract, object message)
    {
        if (validator is null || contract.Kind == RemoteMessageKind.Event)
        {
            return;
        }

        var errors = await validator.Validate(message).ConfigureAwait(false);
        if (errors is { Count: > 0 })
        {
            throw new RaskValidationException(errors);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        RemoteContract contract,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // The timeout is applied HERE, per attempt, rather than on the HttpClient — because on the path
        // most clients take the client is not ours to configure. ResolveHttpClient only sets
        // HttpClient.Timeout when it constructs the client itself, and a same-origin browser app takes
        // the other branch: it reuses the container's HttpClient, whose BaseAddress is the page origin.
        // So options.Timeout was accepted and then disregarded on exactly the default path (#893).
        //
        // Setting Timeout on the shared instance instead would be wrong twice over: the client belongs
        // to the app, and the property throws once a request has been started on it.
        //
        // Scoped to this send, not to the whole method: a chunked upload is many requests and a budget
        // spanning all of them would abort a large, healthy transfer. "Per attempt" is what the option
        // documents.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        HttpResponseMessage response;
        try
        {
            // ResponseHeadersRead so a streamed download is not buffered into memory before the caller
            // ever sees it — the difference between a constant-memory export and one that isn't.
            response = await http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
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
        var path = Endpoint(Uri.EscapeDataString(contract.Name));

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
            await UploadFileAsync(uploadId, index, files[index], buffer, cancellationToken).ConfigureAwait(false);
        }

        return uploadId;
    }

    private async Task UploadFileAsync(
        string uploadId,
        int index,
        RemoteFile file,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        long offset = 0;
        var attemptsLeft = MaxResumeAttempts;
        while (await SendFromAsync(uploadId, index, file, offset, buffer, cancellationToken).ConfigureAwait(false)
               is var (sent, held))
        {
            // Resume from what the server actually holds, in either direction: less than we sent
            // (a chunk was lost) or more (a retry we thought had failed did land).
            //
            // Requiring the offset to CHANGE is what stops this looping: a server repeatedly
            // answering with the offset we are already at is a disagreement retrying cannot fix,
            // so it becomes the failure below rather than spinning until the budget runs
            // out. In-session only — a browser's File handle dies with the page, so a resume
            // across a reload remains impossible and is documented as such.
            if (attemptsLeft == 0 || held == sent)
            {
                // Either way this is a disagreement retrying cannot settle, and it leaves as the same
                // kind of failure every other refused chunk does.
                throw new RemoteDispatchException(
                    "The upload could not resume: the server holds "
                    + $"{held.ToString(CultureInfo.InvariantCulture)} bytes and the "
                    + "client could not reconcile with that.")
                {
                    StatusCode = (int)HttpStatusCode.Conflict,
                };
            }

            attemptsLeft--;
            offset = held;
        }
    }

    // Sends the file from `offset` to its end. Null once every chunk landed; otherwise the offset of the chunk the
    // server turned away and the offset it holds instead.
    private async Task<(long Sent, long Held)?> SendFromAsync(
        string uploadId,
        int index,
        RemoteFile file,
        long offset,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        // Re-opened rather than sought on a resume: an IRaskFile reads in slices on every host —
        // Blob.slice in the browser, a FileStream on the server — and re-opening is the one thing
        // guaranteed to work on both. The file is never materialised whole either way.
        var source = file.OpenReadStream(cancellationToken);
        await using (source.ConfigureAwait(false))
        {
            await SkipAsync(source, offset, buffer, cancellationToken).ConfigureAwait(false);

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (await SendChunkAsync(uploadId, index, offset, file, buffer, read, cancellationToken)
                        .ConfigureAwait(false) is { } held)
                {
                    return (offset, held);
                }

                offset += read;
            }

            return null;
        }
    }

    /// <summary>How many times one file may restart from a server-reported offset before giving up.</summary>
    /// <remarks>
    ///     Bounded rather than open-ended: a resume is for a dropped chunk, and a server that keeps
    ///     disagreeing is a fault to report rather than one to keep paying for. Per file, so a large
    ///     upload of several files does not spend one budget across all of them.
    /// </remarks>
    private const int MaxResumeAttempts = 3;

    /// <summary>Advances <paramref name="source" /> to <paramref name="offset" /> after a resume.</summary>
    private static async Task SkipAsync(
        Stream source,
        long offset,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        if (offset <= 0)
        {
            return;
        }

        if (source.CanSeek)
        {
            source.Seek(offset, SeekOrigin.Begin);
            return;
        }

        // Read-and-discard for a stream that cannot seek. It costs a re-read of what was already sent,
        // which is the price of resuming at all — and still bounded, since the buffer is one chunk.
        var remaining = offset;
        while (remaining > 0)
        {
            var wanted = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                // The file is shorter than the server claims to hold: retrying cannot reconcile that.
                throw new RemoteDispatchException(
                    "The upload could not resume: the file ended before the offset the server reported.");
            }

            remaining -= read;
        }
    }

    /// <summary>The server's <c>X-Rask-Upload-Offset</c>, when it sent a parseable one.</summary>
    private static bool TryReadOffset(HttpResponseMessage response, out long offset)
    {
        offset = 0;
        return response.Headers.TryGetValues(RemoteEndpointDefaults.UploadOffsetHeader, out var values)
               && long.TryParse(
                   values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out offset)
               && offset >= 0;
    }

    // Null once the chunk landed. A 409 answers with the offset the server holds instead — recoverable, unlike every
    // other refusal, which throws.
    private async Task<long?> SendChunkAsync(
        string uploadId,
        int index,
        long offset,
        RemoteFile file,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        using var request = ChunkRequest(uploadId, index, offset, file, buffer, count);

        if (options.ConfigureRequest is { } configure)
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
                return null;
            }

            // 409 is the one refusal that is not the end: the server is saying "not where I am", and it
            // says where it IS in X-Rask-Upload-Offset. That header is the entire point of answering 409
            // rather than 400, and until now the client read neither (#895) — so a single dropped chunk
            // failed an upload the protocol was built to recover.
            if (response.StatusCode == HttpStatusCode.Conflict
                && TryReadOffset(response, out var serverOffset))
            {
                return serverOffset;
            }

            throw new RemoteDispatchException(
                $"The server refused a chunk of the upload ({(int)response.StatusCode}).")
            {
                StatusCode = (int)response.StatusCode,
            };
        }
    }

    private HttpRequestMessage ChunkRequest(
        string uploadId,
        int index,
        long offset,
        RemoteFile file,
        byte[] buffer,
        int count)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(RemoteEndpointDefaults.UploadSegment))
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
        return request;
    }

    // PathBase first: a sub-path deploy (a WASM bundle served under /myapp/) reaches its own host
    // only through that prefix, and the server maps its endpoints under the same one. Without it
    // the request leaves for the site root and 404s — visible only once someone deploys under a path.
    private string Endpoint(string segment) =>
        $"{Rask.Core.Live.LiveOptions.PathBase}{options.RoutePrefix}/{segment}";

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
        var problem = new ProblemDocument(null, null, null, null);

        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                problem = ProblemDocument.Read(payload);
            }
            catch (JsonException)
            {
                // A malformed body is not worth losing the status code over.
            }
        }

        var message = $"'{contract.Name}' failed on the server: {(int)response.StatusCode} {problem.Title ?? response.ReasonPhrase}.";

        // A rejection that names fields is the one failure a form can show where it can be corrected.
        if (problem.Errors is { Count: > 0 } rejected)
        {
            return new RejectedRemoteDispatchException(message, rejected)
            {
                MessageName = contract.Name,
                StatusCode = (int)response.StatusCode,
                ProblemType = problem.Type,
                Detail = problem.Detail,
            };
        }

        return new RemoteDispatchException(message)
        {
            MessageName = contract.Name,
            StatusCode = (int)response.StatusCode,
            ProblemType = problem.Type,
            Detail = problem.Detail,
            Errors = problem.Errors,
        };
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
