using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class ClipboardTests
{
    [Fact]
    public async Task WriteText_SendsClipboardWriteText_WithText()
    {
        var js = new FakeJsRuntime();
        var clipboard = new Clipboard(js);

        await clipboard.WriteTextAsync("hello");

        Assert.Equal(["hello"], js.ArgsFor("navigator.clipboard.writeText"));
    }

    [Fact]
    public async Task ReadText_SendsClipboardReadText_AndReturnsValue()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("navigator.clipboard.readText", "pasted");
        var clipboard = new Clipboard(js);

        var text = await clipboard.ReadTextAsync();

        Assert.Equal("pasted", text);
        Assert.Equal(1, js.CallCount("navigator.clipboard.readText"));
    }

    [Fact]
    public async Task WriteText_Null_Throws()
    {
        var clipboard = new Clipboard(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await clipboard.WriteTextAsync(null!));
    }
}
