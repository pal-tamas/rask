using System.Text;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

// OPFS is the durable home for a local database file, so these pin the exact identifiers and argument
// shapes the __raskOpfs helper is written against — particularly that bytes cross the boundary base64-encoded
// and that a missing path resolves to null rather than throwing.
public class OriginPrivateFileSystemTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskOpfs.isSupported", true);

        Assert.True(await new OriginPrivateFileSystem(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Exists_PassesPath()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskOpfs.exists", true);

        Assert.True(await new OriginPrivateFileSystem(js).ExistsAsync("db/app.sqlite"));
        Assert.Equal(["db/app.sqlite"], js.ArgsFor("__raskOpfs.exists"));
    }

    [Fact]
    public async Task GetSize_ReturnsNull_WhenFileMissing()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new OriginPrivateFileSystem(js).GetSizeAsync("nope.db"));
    }

    [Fact]
    public async Task GetSize_ReturnsSize()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskOpfs.size", (long?)4096);

        Assert.Equal(4096, await new OriginPrivateFileSystem(js).GetSizeAsync("db/app.sqlite"));
    }

    [Fact]
    public async Task Read_PassesRange_AndDecodesBase64()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskOpfs.read", Convert.ToBase64String("page"u8.ToArray()));

        var bytes = await new OriginPrivateFileSystem(js).ReadAsync("db/app.sqlite", 8192, 4096);

        Assert.Equal("page", Encoding.UTF8.GetString(bytes!));
        Assert.Equal(["db/app.sqlite", 8192L, 4096], js.ArgsFor("__raskOpfs.read"));
    }

    [Fact]
    public async Task Read_ReturnsNull_WhenFileMissing()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new OriginPrivateFileSystem(js).ReadAsync("nope.db", 0, 16));
    }

    [Fact]
    public async Task Write_EncodesBytes_AtOffset()
    {
        var js = new FakeJsRuntime();

        await new OriginPrivateFileSystem(js).WriteAsync("db/app.sqlite", 4096, "page"u8.ToArray());

        Assert.Equal(
            ["db/app.sqlite", 4096L, Convert.ToBase64String("page"u8.ToArray())],
            js.ArgsFor("__raskOpfs.write"));
    }

    [Fact]
    public async Task ReadAllBytes_DecodesBase64()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskOpfs.readAll", Convert.ToBase64String("whole"u8.ToArray()));

        var bytes = await new OriginPrivateFileSystem(js).ReadAllBytesAsync("notes.txt");

        Assert.Equal("whole", Encoding.UTF8.GetString(bytes!));
    }

    [Fact]
    public async Task ReadAllBytes_ReturnsNull_WhenFileMissing()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new OriginPrivateFileSystem(js).ReadAllBytesAsync("nope.txt"));
    }

    [Fact]
    public async Task WriteAllBytes_EncodesBytes()
    {
        var js = new FakeJsRuntime();

        await new OriginPrivateFileSystem(js).WriteAllBytesAsync("notes.txt", "whole"u8.ToArray());

        Assert.Equal(
            ["notes.txt", Convert.ToBase64String("whole"u8.ToArray())],
            js.ArgsFor("__raskOpfs.writeAll"));
    }

    [Fact]
    public async Task Truncate_PassesSize()
    {
        var js = new FakeJsRuntime();

        await new OriginPrivateFileSystem(js).TruncateAsync("db/app.sqlite", 0);

        Assert.Equal(["db/app.sqlite", 0L], js.ArgsFor("__raskOpfs.truncate"));
    }

    [Fact]
    public async Task Delete_DefaultsToNonRecursive()
    {
        var js = new FakeJsRuntime();

        await new OriginPrivateFileSystem(js).DeleteAsync("db/app.sqlite");

        Assert.Equal(["db/app.sqlite", false], js.ArgsFor("__raskOpfs.delete"));
    }

    [Fact]
    public async Task Delete_PassesRecursive()
    {
        var js = new FakeJsRuntime();

        await new OriginPrivateFileSystem(js).DeleteAsync("db", recursive: true);

        Assert.Equal(["db", true], js.ArgsFor("__raskOpfs.delete"));
    }

    [Fact]
    public async Task List_DefaultsToRoot()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskOpfs.list", new[] { "db", "notes.txt" });

        Assert.Equal(["db", "notes.txt"], await new OriginPrivateFileSystem(js).ListAsync());
        Assert.Equal([""], js.ArgsFor("__raskOpfs.list"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public async Task Read_Rejects_EmptyPath(string? path)
    {
        var js = new FakeJsRuntime();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await new OriginPrivateFileSystem(js).ReadAsync(path!, 0, 1));
    }

    [Fact]
    public async Task Read_Rejects_NegativeOffset()
    {
        var js = new FakeJsRuntime();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await new OriginPrivateFileSystem(js).ReadAsync("db", -1, 1));
    }

    [Fact]
    public async Task Write_Rejects_NullBytes()
    {
        var js = new FakeJsRuntime();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new OriginPrivateFileSystem(js).WriteAsync("db", 0, null!));
    }
}
