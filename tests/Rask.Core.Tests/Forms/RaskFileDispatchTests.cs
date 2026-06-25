using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Forms;

public class RaskFileDispatchTests
{
    [Fact]
    public async Task ActionWithRaskFileList_Receives_Decoded_Files_And_Releases()
    {
        var backend = new TestBackend();
        var services = new ServiceCollection()
            .AddSingleton<IBrowserFileBackend>(backend)
            .BuildServiceProvider();

        IReadOnlyList<RaskFile>? received = null;
        Callback<IReadOnlyList<RaskFile>> handler = files => received = files;

        var view = new StubComponent(() => Input<string>(OnFiles: handler));
        view.RenderAsLiveRoot();

        var payload = JsonDocument.Parse("""
                                         { "id": "h0", "type": "files", "files": [
                                             { "token": "t1", "name": "a.txt", "size": 5, "type": "text/plain", "lastModified": 1 },
                                             { "token": "t2", "name": "b.txt", "size": 3, "type": "text/plain", "lastModified": 2 }
                                         ]}
                                         """).RootElement;

        var ok = await view.TryInvokeHandlerAsync("h0", payload, services);

        Assert.True(ok);
        Assert.NotNull(received);
        Assert.Equal(2, received!.Count);
        Assert.Equal("a.txt", received[0].Name);
        Assert.Equal(5, received[0].Size);
        Assert.Equal(2, backend.Released.Count);
    }

    [Fact]
    public async Task FuncWithRaskFileList_Async_Receives_Decoded_Files()
    {
        var backend = new TestBackend();
        var services = new ServiceCollection()
            .AddSingleton<IBrowserFileBackend>(backend)
            .BuildServiceProvider();

        var seen = 0;
        CallbackAsync<IReadOnlyList<RaskFile>> handler = files =>
        {
            seen = files.Count;
            return Task.CompletedTask;
        };

        var view = new StubComponent(() => Input<string>(OnFilesAsync: handler));
        view.RenderAsLiveRoot();

        var payload = JsonDocument.Parse("""
                                         { "id": "h0", "type": "files", "files": [
                                             { "token": "x", "name": "x.txt", "size": 1, "type": "text/plain", "lastModified": 0 }
                                         ]}
                                         """).RootElement;

        await view.TryInvokeHandlerAsync("h0", payload, services);

        Assert.Equal(1, seen);
        Assert.Single(backend.Released);
    }

    [Fact]
    public void Input_Emits_DataRaskOnFiles_Attribute_When_OnFiles_Set()
    {
        Callback<IReadOnlyList<RaskFile>> handler = _ => { };
        var view = new StubComponent(() => Input<string>(InputType.File, OnFiles: handler));
        var html = view.RenderAsLiveRoot();
        Assert.Contains("data-rask-on-files=", html);
        Assert.Contains("type=\"file\"", html);
    }

    private sealed class TestBackend : IBrowserFileBackend
    {
        public List<RaskFile> Released { get; } = new();

        public RaskFile Create(JsonElement metadata) => new TestFile(
            metadata.GetProperty("token").GetString() ?? "",
            metadata.GetProperty("name").GetString() ?? "",
            metadata.GetProperty("size").GetInt64(),
            metadata.GetProperty("type").GetString() ?? "application/octet-stream",
            DateTimeOffset.FromUnixTimeMilliseconds(metadata.GetProperty("lastModified").GetInt64()));

        public void Release(IEnumerable<RaskFile> files)
        {
            foreach (var f in files)
            {
                Released.Add(f);
            }
        }
    }

    private sealed class TestFile : RaskFile
    {
        public TestFile(string token, string name, long size, string contentType, DateTimeOffset lastModified)
        {
            Token = token;
            Name = name;
            Size = size;
            ContentType = contentType;
            LastModified = lastModified;
        }

        public string Token { get; }
        public override string Name { get; }
        public override long Size { get; }
        public override string ContentType { get; }
        public override DateTimeOffset LastModified { get; }

        public override Stream OpenReadStream(long maxAllowedSize = 524288,
            CancellationToken cancellationToken = default) =>
            new MemoryStream(new byte[Size]);
    }
}
