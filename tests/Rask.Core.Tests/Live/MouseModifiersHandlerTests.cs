using System.Text.Json;

#pragma warning disable RASK014

namespace Rask.Core.Tests.Live;

public partial class MouseModifiersHandlerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_MouseModifiers_action_receives_the_shift_flag()
    {
        MouseModifiers? captured = null;
        var component = new StubComponent(Span);
        component.RegisterTestHandler("h0", new Action<MouseModifiers>(m => captured = m));

        var payload = JsonDocument.Parse("{\"shiftKey\":true,\"ctrlKey\":false,\"altKey\":false,\"metaKey\":false}")
            .RootElement;
        var ok = await component.TryInvokeHandlerAsync("h0", payload);

        Assert.True(ok);
        Assert.Equal(new MouseModifiers(true, false, false, false), captured);
    }

    [Fact]
    public async Task A_MouseModifiers_action_receives_all_flags_set()
    {
        MouseModifiers? captured = null;
        var component = new StubComponent(Span);
        component.RegisterTestHandler("h0", new Action<MouseModifiers>(m => captured = m));

        var payload = JsonDocument.Parse("{\"shiftKey\":true,\"ctrlKey\":true,\"altKey\":true,\"metaKey\":true}")
            .RootElement;
        await component.TryInvokeHandlerAsync("h0", payload);

        Assert.Equal(new MouseModifiers(true, true, true, true), captured);
    }

    [Fact]
    public async Task Missing_MouseModifiers_fields_default_to_false()
    {
        MouseModifiers? captured = null;
        var component = new StubComponent(Span);
        component.RegisterTestHandler("h0", new Action<MouseModifiers>(m => captured = m));

        var payload = JsonDocument.Parse("{}").RootElement;
        await component.TryInvokeHandlerAsync("h0", payload);

        Assert.Equal(new MouseModifiers(false, false, false, false), captured);
    }

    [Fact]
    public async Task A_task_returning_MouseModifiers_handler_is_awaited_and_receives_the_flags()
    {
        MouseModifiers? captured = null;
        var component = new StubComponent(Span);
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
