using System.Reflection;
using Rask.Core.Forms;
using Rask.Core.Routing;
using Rask.Example.Shared.Pages;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class UploadPageTests
{
    [Fact]
    public void Render_BeforeFileChosen_ShowsNoFileSelected()
    {
        var routeState = new RouteState { Path = "/upload" };
        var html = new Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("upload-input", html);
        Assert.Contains("No file selected yet.", html);
    }

    [Fact]
    public void OnFiles_HydratesMetadataFromFirstFile()
    {
        var page = new UploadPage();
        var onFiles = typeof(UploadPage).GetMethod("OnFiles",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var file = new FakeFile("doc.txt", 12345, "text/plain",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000));
        onFiles.Invoke(page, [new[] { (RaskFile)file }]);

        Assert.Equal("doc.txt", GetField<string?>(page, "_name"));
        Assert.Equal(12345L, GetField<long>(page, "_size"));
        Assert.Equal("text/plain", GetField<string?>(page, "_contentType"));
        Assert.Equal(file.LastModified, GetField<DateTimeOffset>(page, "_modified"));
    }

    [Fact]
    public void OnFiles_EmptyList_ClearsName()
    {
        var page = new UploadPage();
        var onFiles = typeof(UploadPage).GetMethod("OnFiles",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        // Pre-set _name as if a previous file had been chosen.
        SetField(page, "_name", "leftover.txt");

        onFiles.Invoke(page, [Array.Empty<RaskFile>()]);
        Assert.Null(GetField<string?>(page, "_name"));
    }

    private static T GetField<T>(UploadPage page, string name)
    {
        var f = typeof(UploadPage).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var v = f.GetValue(page);
        return v is null ? default! : (T)v;
    }

    private static void SetField(UploadPage page, string name, object? value)
    {
        var f = typeof(UploadPage).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        f.SetValue(page, value);
    }

    // Minimal RaskFile stand-in for the metadata-hydration test.
    private sealed class FakeFile(string name, long size, string contentType, DateTimeOffset lastModified)
        : RaskFile
    {
        public override string Name { get; } = name;
        public override long Size { get; } = size;
        public override string ContentType { get; } = contentType;
        public override DateTimeOffset LastModified { get; } = lastModified;

        public override Stream OpenReadStream(long maxAllowedSize = 512 * 1024,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
