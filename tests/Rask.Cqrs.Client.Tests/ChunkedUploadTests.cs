using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Client.Tests;

/// <summary>
///     What the client does with a file too large to ride in one request.
/// </summary>
/// <remarks>
///     A browser's <c>fetch</c> reads a request body into memory before sending it, so a single-shot
///     upload costs its own size in the tab. Every host already reads a <c>RaskFile</c> in bounded slices
///     — chunking is what keeps the <em>request</em> bounded too, and it is the whole reason this path
///     exists rather than a bigger multipart body.
/// </remarks>
public sealed class ChunkedUploadTests
{
    [Fact]
    public async Task A_small_file_still_rides_along_with_the_message()
    {
        // One request, multipart. Chunking a small file would cost a round trip for nothing.
        var handler = new RecordingHandler();
        var file = new PickedFile("small.txt", "text/plain", new byte[64]);

        await Dispatcher(handler).SendAsync(new AttachToThing(1, file));

        Assert.Single(handler.Requests);
        Assert.Contains("multipart/form-data", handler.Requests[0].ContentType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_large_file_goes_up_in_chunks_before_the_message()
    {
        var handler = new RecordingHandler();
        var file = new PickedFile("big.bin", "application/octet-stream", new byte[10 * 1024]);

        await Dispatcher(handler, o =>
        {
            o.ChunkedUploadThreshold = 1024;
            o.UploadChunkSize = 4096;
        }).SendAsync(new AttachToThing(1, file));

        // 10 KB in 4 KB chunks is 3 requests, then the message: the last chunk is deliberately short, so
        // an off-by-one in the offset arithmetic cannot hide behind round numbers.
        Assert.Equal(4, handler.Requests.Count);

        var chunks = handler.Requests.Take(3).ToList();
        Assert.All(chunks, r => Assert.EndsWith(
            "/" + RemoteEndpointDefaults.UploadSegment, r.Path, StringComparison.Ordinal));
        Assert.Equal(new[] { "0", "4096", "8192" }, chunks.Select(c => c.Offset ?? string.Empty).ToArray());
        Assert.Equal(new[] { 4096, 4096, 2048 }, chunks.Select(c => c.Length).ToArray());

        // No request ever carries more than one chunk: that bound IS the feature.
        Assert.All(chunks, r => Assert.True(r.Length <= 4096));

        // The message follows as plain JSON, naming the session rather than carrying the bytes again.
        var message = handler.Requests[^1];
        Assert.Contains("AttachToThing", message.Path, StringComparison.Ordinal);
        Assert.Contains("application/json", message.ContentType, StringComparison.Ordinal);
        Assert.NotNull(message.UploadId);
        Assert.All(chunks, r => Assert.Equal(message.UploadId, r.UploadId));
    }

    [Fact]
    public async Task A_chunk_carries_the_files_name_url_encoded()
    {
        // A filename is user input: raw, it can hold CR/LF or non-ASCII, neither of which belongs in a
        // header value. It is also what the handler sees, so it has to survive the trip.
        var handler = new RecordingHandler();
        var file = new PickedFile("mes notes.txt", "text/plain", new byte[4096]);

        await Dispatcher(handler, o =>
        {
            o.ChunkedUploadThreshold = 1024;
            o.UploadChunkSize = 4096;
        }).SendAsync(new AttachToThing(1, file));

        Assert.Equal("mes%20notes.txt", handler.Requests[0].FileName);
    }

    [Fact]
    public async Task A_refused_chunk_fails_the_dispatch_rather_than_sending_a_message_without_it()
    {
        // Sending the message anyway would ask the server to resolve files that were never assembled —
        // and the handler would be given a truncated file, or none, with nothing to say so.
        var handler = new RecordingHandler(HttpStatusCode.Forbidden);
        var file = new PickedFile("big.bin", "application/octet-stream", new byte[4096]);

        var dispatcher = Dispatcher(handler, o =>
        {
            o.ChunkedUploadThreshold = 1024;
            o.UploadChunkSize = 1024;
        });

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => dispatcher.SendAsync(new AttachToThing(1, file)));

        Assert.Equal(403, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    /// <summary>
    ///     A 409 is the one refusal the client recovers from, because the server says where it is.
    /// </summary>
    /// <remarks>
    ///     The server has always echoed <c>X-Rask-Upload-Offset</c> and answered 409 on a mismatch —
    ///     409 rather than 400 precisely so the offset can ride along — and the client read neither, so a
    ///     single dropped chunk failed an upload the protocol was built to recover (#895). The affordance
    ///     cost server code and a documented status code while delivering nothing.
    /// </remarks>
    [Fact]
    public async Task A_dropped_chunk_resumes_from_the_offset_the_server_reports()
    {
        // Rejects the chunk at 1024 once, claiming to hold only 512 — so the client must go back and
        // resend from there rather than failing the whole upload.
        var handler = new RecordingHandler(rejectOnce: (Offset: 1024, ServerHolds: 512));
        var file = new PickedFile("big.bin", "application/octet-stream", new byte[4096]);

        await Dispatcher(handler, o =>
        {
            o.ChunkedUploadThreshold = 1024;
            o.UploadChunkSize = 1024;
        }).SendAsync(new AttachToThing(1, file));

        var offsets = handler.Requests
            .Where(r => r.Offset is not null)
            .Select(r => r.Offset!)
            .ToArray();

        // 1024 is refused, then the client restarts at the server's 512 and carries on to the end.
        Assert.Equal(["0", "1024", "512", "1536", "2560", "3584"], offsets);

        // And the message itself still goes, after the file is whole — the point of resuming at all.
        Assert.Contains(handler.Requests, r => r.Offset is null);
    }

    [Fact]
    public async Task A_server_that_keeps_reporting_the_same_offset_fails_instead_of_looping()
    {
        // The offset must CHANGE for a retry to be worth making. A server answering 409 with the offset
        // the client is already at is a disagreement retrying cannot fix, so it has to surface rather
        // than spin until a budget runs out.
        var handler = new RecordingHandler(rejectOnce: (Offset: 1024, ServerHolds: 1024), always: true);
        var file = new PickedFile("big.bin", "application/octet-stream", new byte[4096]);

        var dispatcher = Dispatcher(handler, o =>
        {
            o.ChunkedUploadThreshold = 1024;
            o.UploadChunkSize = 1024;
        });

        var error = await Assert.ThrowsAsync<RemoteDispatchException>(
            () => dispatcher.SendAsync(new AttachToThing(1, file)));

        Assert.Equal(409, error.StatusCode);
    }

    private static IDispatcher Dispatcher(RecordingHandler handler, Action<RaskCqrsClientOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://unit.test/") });
        services.AddRaskCqrsClient(configure);
        return services.BuildServiceProvider().GetRequiredService<IDispatcher>();
    }

    private sealed record Captured(
        string Path, string ContentType, int Length, string? UploadId, string? Offset, string? FileName);

    /// <param name="rejectOnce">
    ///     Answer 409 for the chunk at <c>offset</c>, reporting <c>serverHolds</c> in
    ///     <c>X-Rask-Upload-Offset</c> — a real server's shape for "that does not follow on from what I
    ///     have". Fired once unless <paramref name="always" /> is set.
    /// </param>
    /// <param name="always">Keep rejecting, to prove a client that cannot make progress gives up.</param>
    private sealed class RecordingHandler(
        HttpStatusCode chunkStatus = HttpStatusCode.NoContent,
        (long Offset, long ServerHolds)? rejectOnce = null,
        bool always = false)
        : HttpMessageHandler
    {
        private bool _rejected;

        public List<Captured> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            Requests.Add(new Captured(
                request.RequestUri!.AbsolutePath,
                request.Content?.Headers.ContentType?.ToString() ?? string.Empty,
                body.Length,
                Header(request, RemoteEndpointDefaults.UploadHeader),
                Header(request, RemoteEndpointDefaults.UploadOffsetHeader),
                Header(request, RemoteEndpointDefaults.UploadNameHeader)));

            var isChunk = request.RequestUri.AbsolutePath.EndsWith(
                RemoteEndpointDefaults.UploadSegment, StringComparison.Ordinal);

            if (isChunk && rejectOnce is { } reject && (always || !_rejected)
                && Header(request, RemoteEndpointDefaults.UploadOffsetHeader)
                    == reject.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                _rejected = true;

                // What the server sends: 409, with the offset it actually holds. The header is the whole
                // reason the status is 409 rather than 400.
                var conflict = new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"title\":\"Offset mismatch\"}"),
                };
                conflict.Headers.TryAddWithoutValidation(
                    RemoteEndpointDefaults.UploadOffsetHeader,
                    reject.ServerHolds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return conflict;
            }

            return new HttpResponseMessage(isChunk ? chunkStatus : HttpStatusCode.OK)
            {
                Content = new StringContent("\"ok\""),
            };
        }

        private static string? Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }

    /// <summary>What a file input hands a component — the type a message declares, on every host.</summary>
    private sealed class PickedFile(string name, string contentType, byte[] bytes) : RaskFile
    {
        public override string Name => name;

        public override long Size => bytes.Length;

        public override string ContentType => contentType;

        public override DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public override Stream OpenReadStream(
            long maxAllowedSize = 512 * 1024,
            CancellationToken cancellationToken = default) =>
            bytes.Length > maxAllowedSize
                ? throw new IOException($"'{name}' is {bytes.Length} bytes, over the {maxAllowedSize} ceiling.")
                : new MemoryStream(bytes, writable: false);
    }
}
