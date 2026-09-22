using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class ClipboardTests
{
    [Fact]
    public async Task Writing_text_sends_the_clipboard_write_with_the_text()
    {
        var js = new FakeJsRuntime();
        var clipboard = new Clipboard(js);

        await clipboard.WriteTextAsync("hello");

        Assert.Equal(["hello"], js.ArgsFor("navigator.clipboard.writeText"));
    }

    [Fact]
    public async Task Reading_text_sends_the_clipboard_read_and_gives_the_value()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("navigator.clipboard.readText", "pasted");
        var clipboard = new Clipboard(js);

        var text = await clipboard.ReadTextAsync();

        Assert.Equal("pasted", text);
        Assert.Equal(1, js.CallCount("navigator.clipboard.readText"));
    }

    [Fact]
    public async Task Writing_null_text_throws()
    {
        var clipboard = new Clipboard(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await clipboard.WriteTextAsync(null!));
    }
}
