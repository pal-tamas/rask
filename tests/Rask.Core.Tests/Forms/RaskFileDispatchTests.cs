using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Forms;

public partial class RaskFileDispatchTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_sync_files_handler_receives_the_decoded_files_and_releases_them()
    {
        var backend = new TestBackend();
        var services = new ServiceCollection()
            .AddSingleton<IBrowserFileBackend>(backend)
            .BuildServiceProvider();
        IReadOnlyList<IRaskFile>? received = null;
        Action<IReadOnlyList<IRaskFile>> handler = files => received = files;
        var page = Page.Render(() => Input.Value<string>(null).OnFiles(handler), services);

        var ok = await page.TryInvoke("h0", """
                                                 { "id": "h0", "type": "files", "files": [
                                                     { "token": "t1", "name": "a.txt", "size": 5, "type": "text/plain", "lastModified": 1 },
                                                     { "token": "t2", "name": "b.txt", "size": 3, "type": "text/plain", "lastModified": 2 }
                                                 ]}
                                                 """);

        Assert.True(ok);
        Assert.NotNull(received);
        Assert.Equal(2, received!.Count);
        Assert.Equal("a.txt", received[0].Name);
        Assert.Equal(5, received[0].Size);
        Assert.Equal(2, backend.Released.Count);
    }

    [Fact]
    public async Task An_async_files_handler_receives_the_decoded_files()
    {
        var backend = new TestBackend();
        var services = new ServiceCollection()
            .AddSingleton<IBrowserFileBackend>(backend)
            .BuildServiceProvider();
        var seen = 0;
        Func<IReadOnlyList<IRaskFile>, Task> handler = files =>
        {
            seen = files.Count;
            return Task.CompletedTask;
        };
        var page = Page.Render(() => Input.Value<string>(null).OnFiles(handler), services);

        await page.Invoke("h0", """
                                     { "id": "h0", "type": "files", "files": [
                                         { "token": "x", "name": "x.txt", "size": 1, "type": "text/plain", "lastModified": 0 }
                                     ]}
                                     """);

        Assert.Equal(1, seen);
        Assert.Single(backend.Released);
    }

    [Fact]
    public void An_input_with_OnFiles_set_emits_the_files_handler_attribute()
    {
        Action<IReadOnlyList<IRaskFile>> handler = _ => { };
        var html = Page.Render(() => Input.Value<string>(null).Type(InputType.File).OnFiles(handler)).Html;

        Assert.Contains("data-rask-on-files=", html);
        Assert.Contains("type=\"file\"", html);
    }

    private sealed class TestBackend : IBrowserFileBackend
    {
        public List<IRaskFile> Released { get; } = new();

        public IRaskFile Create(JsonElement metadata) => new TestFile(
            metadata.GetProperty("token").GetString() ?? "",
            metadata.GetProperty("name").GetString() ?? "",
            metadata.GetProperty("size").GetInt64(),
            metadata.GetProperty("type").GetString() ?? "application/octet-stream",
            DateTimeOffset.FromUnixTimeMilliseconds(metadata.GetProperty("lastModified").GetInt64()));

        public void Release(IEnumerable<IRaskFile> files)
        {
            foreach (var f in files)
            {
                Released.Add(f);
            }
        }
    }

    private sealed class TestFile : IRaskFile
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
        public string Name { get; }
        public long Size { get; }
        public string ContentType { get; }
        public DateTimeOffset LastModified { get; }

        public Stream OpenReadStream(long maxAllowedSize = 524288,
            CancellationToken cancellationToken = default) =>
            new MemoryStream(new byte[Size]);
    }
}
