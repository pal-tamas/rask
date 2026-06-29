using System.Text;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class FileSystemAccessTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.isSupported", true);

        Assert.True(await new FileSystemAccess(js).IsSupportedAsync());
    }

    [Fact]
    public async Task OpenFile_ReturnsHandle_WithName()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.openFile", new FileSystemHandleInfo(7, "notes.txt"));

        var handle = await new FileSystemAccess(js).OpenFileAsync();

        Assert.NotNull(handle);
        Assert.Equal("notes.txt", handle!.Name);
    }

    [Fact]
    public async Task OpenFile_ReturnsNull_WhenCancelled()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new FileSystemAccess(js).OpenFileAsync());
    }

    [Fact]
    public async Task OpenFiles_ReturnsEmpty_WhenCancelled()
    {
        var js = new FakeJsRuntime();

        Assert.Empty(await new FileSystemAccess(js).OpenFilesAsync());
    }

    [Fact]
    public async Task SaveFile_PassesOptions()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.saveFile", new FileSystemHandleInfo(1, "out.txt"));
        var options = new SaveFilePickerOptions { SuggestedName = "out.txt" };

        await new FileSystemAccess(js).SaveFileAsync(options);

        Assert.Same(options, js.ArgsFor("__raskFs.saveFile")![0]);
    }

    [Fact]
    public async Task ReadText_PassesHandleId()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.openFile", new FileSystemHandleInfo(42, "a.txt"));
        js.SetResponse("__raskFs.readText", "hello");
        var handle = await new FileSystemAccess(js).OpenFileAsync();

        var text = await handle!.ReadTextAsync();

        Assert.Equal("hello", text);
        Assert.Equal([42], js.ArgsFor("__raskFs.readText"));
    }

    [Fact]
    public async Task WriteText_PassesIdAndText()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.openFile", new FileSystemHandleInfo(42, "a.txt"));
        var handle = await new FileSystemAccess(js).OpenFileAsync();

        await handle!.WriteTextAsync("updated");

        Assert.Equal([42, "updated"], js.ArgsFor("__raskFs.writeText"));
    }

    [Fact]
    public async Task ReadBytes_DecodesBase64()
    {
        var bytes = Encoding.UTF8.GetBytes("rask");
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.openFile", new FileSystemHandleInfo(5, "a.bin"));
        js.SetResponse("__raskFs.readBytes", Convert.ToBase64String(bytes));
        var handle = await new FileSystemAccess(js).OpenFileAsync();

        Assert.Equal(bytes, await handle!.ReadBytesAsync());
    }

    [Fact]
    public async Task WriteBytes_EncodesBase64()
    {
        var bytes = Encoding.UTF8.GetBytes("rask");
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.openFile", new FileSystemHandleInfo(5, "a.bin"));
        var handle = await new FileSystemAccess(js).OpenFileAsync();

        await handle!.WriteBytesAsync(bytes);

        Assert.Equal([5, Convert.ToBase64String(bytes)], js.ArgsFor("__raskFs.writeBytes"));
    }

    [Fact]
    public async Task Dispose_ReleasesHandle_Once()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.openFile", new FileSystemHandleInfo(9, "a.txt"));
        var handle = await new FileSystemAccess(js).OpenFileAsync();

        await handle!.DisposeAsync();
        await handle.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskFs.release"));
        Assert.Equal([9], js.ArgsFor("__raskFs.release"));
    }

    [Fact]
    public async Task OpenDirectory_ListAndGetFile_PassIds()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.openDirectory", new FileSystemHandleInfo(3, "docs"));
        js.SetResponse("__raskFs.list", new[] { "a.txt", "b.txt" });
        js.SetResponse("__raskFs.getFile", new FileSystemHandleInfo(4, "a.txt"));
        var dir = await new FileSystemAccess(js).OpenDirectoryAsync();

        Assert.Equal("docs", dir!.Name);
        Assert.Equal(["a.txt", "b.txt"], await dir.ListAsync());
        Assert.Equal([3], js.ArgsFor("__raskFs.list"));

        var file = await dir.GetFileAsync("a.txt", create: true);
        Assert.Equal("a.txt", file.Name);
        Assert.Equal([3, "a.txt", true], js.ArgsFor("__raskFs.getFile"));
    }

    [Fact]
    public async Task WriteText_NullText_Throws()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFs.openFile", new FileSystemHandleInfo(1, "a.txt"));
        var handle = await new FileSystemAccess(js).OpenFileAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await handle!.WriteTextAsync(null!));
    }
}
