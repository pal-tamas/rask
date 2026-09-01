using System.Text;
using Rask.Cqrs.Client;

namespace Rask.Cqrs.Transport.Tests;

/// <summary>
///     Files, both directions, across the real pair. This is where the two halves have the most to
///     disagree about and the least shared code to keep them honest: the client writes a multipart body
///     or drives an upload session, and the server parses one or reassembles the other.
/// </summary>
public sealed class FileWireTests
{
    [Fact]
    public async Task A_small_file_rides_along_as_multipart_and_reaches_the_handler_intact()
    {
        await using var wire = Wire.Connect();

        var answer = await wire.SendAsync<string>(
            new Attach("receipt", new PickedFile("march.csv", "text/csv", "id,total\n1,9"u8.ToArray())));

        Assert.Equal("receipt|march.csv|text/csv|id,total\n1,9", answer);
        Assert.Equal(HttpMethod.Post, wire.Recorder.Last.Method);
        Assert.Equal("multipart/form-data", wire.Recorder.Last.ContentType);
    }

    [Fact]
    public async Task Two_files_arrive_paired_to_the_properties_they_were_picked_for()
    {
        // The part name is the index the JSON wrote, not the file's name. Nothing but a round trip shows
        // the server pairs them back the same way round.
        await using var wire = Wire.Connect();

        var answer = await wire.SendAsync<string>(new AttachTwo(
            new PickedFile("first.txt", "text/plain", "one"u8.ToArray()),
            new PickedFile("second.txt", "text/plain", "two"u8.ToArray())));

        Assert.Equal("first.txt=one;second.txt=two", answer);
    }

    [Fact]
    public async Task A_file_over_the_threshold_goes_up_in_chunks_before_the_message_does()
    {
        // Above ChunkedUploadThreshold the bytes leave first, in UploadChunkSize pieces, and the message
        // follows carrying only the session id. The handler must not be able to tell which route the file
        // took — same name, same type, same bytes. The name and content type reach it through headers on
        // the chunks rather than through the multipart part, which is a second encoding to agree on.
        await using var wire = Wire.Connect(configureClient: Chunked);

        var payload = new string('a', 40);
        var answer = await wire.SendAsync<string>(
            new Attach("bulk", new PickedFile("big.txt", "text/plain", Encoding.UTF8.GetBytes(payload))));

        Assert.Equal($"bulk|big.txt|text/plain|{payload}", answer);

        // 40 bytes in 8-byte chunks, then the message: the request log is the evidence that the message
        // did not carry the body.
        Assert.Equal(5, wire.Recorder.Chunks);
        Assert.Equal("application/json", wire.Recorder.Last.ContentType);
    }

    [Fact]
    public async Task A_dropped_chunk_resumes_from_the_offset_the_server_reports()
    {
        // The #895 fix, end to end. One chunk is answered 200 without ever reaching the server, so the
        // client believes it landed and sends the next one from an offset the server does not hold. The
        // server answers 409 carrying the offset it DOES hold, and the client restarts from there.
        //
        // Neither half can show this alone: the client's suite has to invent the 409 and the offset
        // header on it, and the server's suite has to invent the recovery.
        await using var wire = Wire.Connect(configureClient: Chunked);
        wire.Recorder.DropChunk = 2;

        var payload = new string('b', 40);
        var answer = await wire.SendAsync<string>(
            new Attach("resumed", new PickedFile("big.txt", "text/plain", Encoding.UTF8.GetBytes(payload))));

        Assert.Equal($"resumed|big.txt|text/plain|{payload}", answer);
        Assert.True(wire.Recorder.Dropped, "the harness never dropped a chunk, so nothing was resumed");
    }

    [Fact]
    public async Task A_download_comes_back_named_and_typed_the_way_the_handler_returned_it()
    {
        // The response IS the file: no JSON envelope, a Content-Disposition the client reads the name
        // from, and a body the caller streams. The name makes the round trip through a header the server
        // reduces to a safe leaf and the client unquotes — two transformations, one on each side.
        await using var wire = Wire.Connect();

        var download = await wire.SendAsync<FileDownload>(new Export(2026));

        Assert.Equal("orders-2026.csv", download.FileName);
        Assert.Equal("text/csv", download.ContentType);

        using var reader = new StreamReader(download.OpenReadStream());
        Assert.Equal("id,year\n1,2026", await reader.ReadToEndAsync());
    }

    // Small enough that a readable test file is over it, so the chunked route is taken by a payload a
    // failure message can print.
    private static void Chunked(RaskCqrsClientOptions options)
    {
        options.ChunkedUploadThreshold = 8;
        options.UploadChunkSize = 8;
    }
}
