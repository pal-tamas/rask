using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Rask.Native.Files;

namespace Rask.Native.Tests.Files;

// File input is one of the Core contracts that silently did nothing on this host: with no
// IBrowserFileBackend registered, FileListReader handed the handler an empty list, so a user picked a file
// and the app reported success having uploaded nothing.
public sealed class NativeFileBackendTests
{
    private static readonly byte[] Content = "the quick brown fox jumps over the lazy dog"u8.ToArray();

    [Fact]
    public void Create_ReadsTheMetadataTheClientSent()
    {
        var file = new NativeFileBackend(new FakeJsRuntime(Content)).Create(Meta("r1", "notes.txt", 42));

        Assert.Equal("notes.txt", file.Name);
        Assert.Equal(42, file.Size);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000), file.LastModified);
    }

    [Fact]
    public void Create_WithoutARef_ThrowsRatherThanReturningAnUnreadableFile()
    {
        var backend = new NativeFileBackend(new FakeJsRuntime(Content));

        var ex = Assert.Throws<InvalidOperationException>(
            () => backend.Create(JsonDocument.Parse("""{"name":"x.txt","size":1}""").RootElement));

        Assert.Contains("ref", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenReadStream_ReadsTheWholeFileBackInChunks()
    {
        var js = new FakeJsRuntime(Content);
        var file = new NativeFileBackend(js).Create(Meta("r1", "fox.txt", Content.Length));

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        Assert.Equal(Encoding.UTF8.GetString(Content), Encoding.UTF8.GetString(buffer.ToArray()));
        // Reading over the bridge at all is the point: nothing was inlined into the render payload.
        Assert.NotEmpty(js.Calls);
    }

    [Fact]
    public void OpenReadStream_RefusesAFileOverTheCallersLimit()
    {
        var file = new NativeFileBackend(new FakeJsRuntime(Content)).Create(Meta("r1", "big.bin", 1024));

        Assert.Throws<IOException>(() => file.OpenReadStream(512));
    }

    // The registry drops a ref when the user re-picks on the same input. Ending the stream short is honest;
    // faulting it would turn a race the user caused into an app-level exception.
    [Fact]
    public async Task AVanishedRef_EndsTheStreamInsteadOfThrowing()
    {
        var js = new FakeJsRuntime(Content) { Vanished = true };
        var file = new NativeFileBackend(js).Create(Meta("r1", "gone.txt", Content.Length));

        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(new byte[64]);

        Assert.Equal(0, read);
    }

    private static JsonElement Meta(string @ref, string name, long size) =>
        JsonDocument.Parse(
                $$"""
                  {"ref":"{{@ref}}","name":"{{name}}","size":{{size}},
                   "type":"text/plain","lastModified":1700000000000}
                  """)
            .RootElement;

    // Stands in for the WebView's rask-files.js: answers __raskFiles.readChunkBase64 out of a byte array.
    private sealed class FakeJsRuntime(byte[] content) : IJSRuntime
    {
        public List<string> Calls { get; } = [];
        public bool Vanished { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken,
            object?[]? args)
        {
            Calls.Add(identifier);
            Assert.Equal("__raskFiles.readChunkBase64", identifier);
            if (Vanished)
            {
                return ValueTask.FromResult((TValue)(object)string.Empty);
            }

            var offset = (int)Convert.ToInt64(args![1]!);
            var length = Convert.ToInt32(args[2]!);
            var slice = content.AsSpan(offset, Math.Min(length, content.Length - offset)).ToArray();
            return ValueTask.FromResult((TValue)(object)Convert.ToBase64String(slice));
        }
    }
}
