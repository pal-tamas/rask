using System.Text.Json;
using Rask.Core.Components;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Live;

public class MouseModifiersHandlerTests
{
    [Fact]
    public async Task ActionMouseModifiers_ReceivesShiftFlag()
    {
        MouseModifiers? captured = null;
        var component = new StubComponent(Span());
        component.RegisterTestHandler("h0", new Action<MouseModifiers>(m => captured = m));

        var payload = JsonDocument.Parse("{\"shiftKey\":true,\"ctrlKey\":false,\"altKey\":false,\"metaKey\":false}").RootElement;
        var ok = await component.TryInvokeHandlerAsync("h0", payload);

        Assert.True(ok);
        Assert.Equal(new MouseModifiers(true, false, false, false), captured);
    }

    [Fact]
    public async Task ActionMouseModifiers_AllFlagsSet()
    {
        MouseModifiers? captured = null;
        var component = new StubComponent(Span());
        component.RegisterTestHandler("h0", new Action<MouseModifiers>(m => captured = m));

        var payload = JsonDocument.Parse("{\"shiftKey\":true,\"ctrlKey\":true,\"altKey\":true,\"metaKey\":true}").RootElement;
        await component.TryInvokeHandlerAsync("h0", payload);

        Assert.Equal(new MouseModifiers(true, true, true, true), captured);
    }

    [Fact]
    public async Task ActionMouseModifiers_MissingFields_DefaultToFalse()
    {
        MouseModifiers? captured = null;
        var component = new StubComponent(Span());
        component.RegisterTestHandler("h0", new Action<MouseModifiers>(m => captured = m));

        var payload = JsonDocument.Parse("{}").RootElement;
        await component.TryInvokeHandlerAsync("h0", payload);

        Assert.Equal(new MouseModifiers(false, false, false, false), captured);
    }

    [Fact]
    public async Task FuncMouseModifiersTask_IsAwaited_AndReceivesFlags()
    {
        MouseModifiers? captured = null;
        var component = new StubComponent(Span());
        component.RegisterTestHandler("h0", new Func<MouseModifiers, Task>(async m =>
        {
            await Task.Yield();
            captured = m;
        }));

        var payload = JsonDocument.Parse("{\"shiftKey\":true}").RootElement;
        var ok = await component.TryInvokeHandlerAsync("h0", payload);

        Assert.True(ok);
        Assert.Equal(new MouseModifiers(true, false, false, false), captured);
    }
}
