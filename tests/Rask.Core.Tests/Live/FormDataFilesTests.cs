using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Forms;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public class FormDataFilesTests
{
    [Fact]
    public void FromJson_ParsesScalars_AndFilesViaBackend()
    {
        var backend = new TestBackend();
        var services = new ServiceCollection()
            .AddSingleton<IBrowserFileBackend>(backend)
            .BuildServiceProvider();

        using var scope = DispatchServicesScope.Push(services);

        var payload = JsonDocument.Parse("""
                                         { "form": {
                                             "name": "Ada",
                                             "__files": {
                                                 "avatar": [{ "token": "t1", "name": "a.png", "size": 100, "type": "image/png", "lastModified": 1 }]
                                             }
                                         }}
                                         """).RootElement;

        var fd = FormData.FromJson(payload);

        Assert.Equal("Ada", fd.Get("name"));
        Assert.True(fd.HasFiles("avatar"));
        Assert.Equal("a.png", fd.Files("avatar")[0].Name);
        Assert.False(fd.HasFiles("missing"));
        Assert.Empty(fd.Files("missing"));
    }

    [Fact]
    public void FromJson_NoFilesBlock_ReturnsEmpty()
    {
        var payload = JsonDocument.Parse("""{ "form": { "x": "y" } }""").RootElement;
        var fd = FormData.FromJson(payload);
        Assert.False(fd.HasFiles("anything"));
    }

    private sealed class TestBackend : IBrowserFileBackend
    {
        public RaskFile Create(JsonElement metadata) => new TestFile(
            metadata.GetProperty("name").GetString() ?? "",
            metadata.GetProperty("size").GetInt64());

        public void Release(IEnumerable<RaskFile> files) { }
    }

    private sealed class TestFile : RaskFile
    {
        public TestFile(string name, long size)
        {
            Name = name;
            Size = size;
        }

        public override string Name { get; }
        public override long Size { get; }
        public override string ContentType => "application/octet-stream";
        public override DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public override Stream OpenReadStream(long maxAllowedSize = 524288,
            CancellationToken cancellationToken = default) =>
            Stream.Null;
    }
}
