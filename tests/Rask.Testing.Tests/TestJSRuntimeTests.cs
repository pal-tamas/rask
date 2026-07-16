#pragma warning disable RASK014 // test-defined components constructed directly

using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Rask.Testing.Tests;

// A component that injects IJSRuntime is untestable without a fake, so the package ships one. These pin
// the record-and-replay contract a consumer relies on.
public class TestJSRuntimeTests
{
    private sealed class Copier(IJSRuntime js) : Component
    {
        public string? Read { get; private set; }

        protected override Component? Render() =>
            Button(Type: "button", OnClick: async () =>
            {
                await js.InvokeVoidAsync("raskApi.clipboard.write", "hello");
                Read = await js.InvokeAsync<string>("raskApi.clipboard.read");
            })["copy"];
    }

    private static (RenderedComponent<Copier> Page, TestJSRuntime Js) RenderCopier()
    {
        var js = new TestJSRuntime();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(js).BuildServiceProvider();
        return (RaskTest.Render(new Copier(js), services), js);
    }

    [Fact]
    public async Task RecordsTheIdentifierAndArgumentsAComponentInvoked()
    {
        var (page, js) = RenderCopier();

        await page.ClickAsync();

        Assert.Equal(["hello"], js.ArgsFor("raskApi.clipboard.write"));
        Assert.Equal(1, js.CallCount("raskApi.clipboard.write"));
    }

    [Fact]
    public async Task SetResponse_IsHandedBackToTheComponent()
    {
        var (page, js) = RenderCopier();
        js.SetResponse("raskApi.clipboard.read", "from-the-clipboard");

        await page.ClickAsync();

        Assert.Equal("from-the-clipboard", page.Instance.Read);
    }

    [Fact]
    public async Task UnconfiguredCall_ReturnsDefault_RatherThanThrowing()
    {
        var (page, js) = RenderCopier();

        await page.ClickAsync();

        // An absent value reads back as null, the same as a real empty clipboard/storage slot.
        Assert.Null(page.Instance.Read);
        Assert.Equal(1, js.CallCount("raskApi.clipboard.read"));
    }

    [Fact]
    public async Task SetException_FaultsTheCall()
    {
        var js = new TestJSRuntime();
        js.SetException("boom", new InvalidOperationException("no"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await js.InvokeAsync<string>("boom", []));
    }

    [Fact]
    public async Task Calls_AreRecordedInInvocationOrder()
    {
        var (page, js) = RenderCopier();

        await page.ClickAsync();

        // Order is the contract: the write must be observable as having happened before the read.
        Assert.Equal(
            ["raskApi.clipboard.write", "raskApi.clipboard.read"],
            js.Calls.Select(c => c.Identifier));
    }

    [Fact]
    public async Task ArgsFor_WhenCalledMoreThanOnce_SaysSoInsteadOfThrowingAnOpaqueSequenceError()
    {
        var (page, js) = RenderCopier();
        await page.ClickAsync();
        await page.ClickAsync();

        var ex = Assert.Throws<InvalidOperationException>(() => js.ArgsFor("raskApi.clipboard.write"));
        Assert.Contains("exactly one call", ex.Message);
        Assert.Contains("there were 2", ex.Message);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var js = new TestJSRuntime();

        Assert.Throws<ArgumentNullException>(() => js.SetResponse(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => js.SetException("id", null!));
        Assert.Throws<ArgumentNullException>(() => js.ArgsFor(null!));
        Assert.Throws<ArgumentNullException>(() => js.CallCount(null!));
    }
}
