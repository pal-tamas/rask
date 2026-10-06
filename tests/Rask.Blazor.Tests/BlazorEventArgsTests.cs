using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Testing;
using Web = Microsoft.AspNetCore.Components.Web;

namespace Rask.Blazor.Tests;

/// <summary>
///     A hosted handler receives the event it asked for: the browser's own fields, in Blazor's own type.
/// </summary>
public partial class BlazorEventArgsTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services(ILoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        if (logs is not null)
        {
            services.AddLogging(b => b.AddProvider(logs));
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_hosted_keydown_handler_receives_the_pressed_key()
    {
        var page = Page.Render(EventIsland, Services());

        await page.On("input").Raise("keydown", """{"type":"keydown","key":"Enter","code":"NumpadEnter","shiftKey":true}""");

        Assert.Contains("seen: keydown Enter NumpadEnter shift True", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosted_click_handler_receives_the_pointer_position_and_modifier_keys()
    {
        var page = Page.Render(EventIsland, Services());

        await page.On("button").Raise("click", """{"type":"click","clientX":12,"clientY":34,"ctrlKey":true,"button":2}""");

        Assert.Contains("seen: click at 12,34 button 2 ctrl True alt False", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_handler_expecting_PointerEventArgs_is_never_handed_its_base_type()
    {
        var mouse = new MouseEvent { ClientX = 5 };
        var pointer = new PointerEvent { ClientX = 5, PointerType = "touch", PointerId = 7 };

        var derived = BlazorEventArgs.From(typeof(Web.PointerEventArgs), mouse);
        var exact = BlazorEventArgs.From(typeof(Web.PointerEventArgs), pointer);
        var narrowed = BlazorEventArgs.From(typeof(Web.MouseEventArgs), pointer);

        Assert.Equal(5, Assert.IsType<Web.PointerEventArgs>(derived).ClientX);
        Assert.Equal(("touch", 7L), (Assert.IsType<Web.PointerEventArgs>(exact).PointerType, ((Web.PointerEventArgs)exact).PointerId));
        Assert.IsType<Web.MouseEventArgs>(narrowed);
    }

    [Fact]
    public async Task A_hosted_wheel_handler_receives_the_scroll_deltas()
    {
        var page = Page.Render(EventIsland, Services());

        await page.On("section").Raise("wheel", """{"type":"wheel","deltaX":1.5,"deltaY":-120,"deltaMode":1,"clientX":8}""");

        Assert.Contains("seen: wheel 1.5,-120 mode 1 at 8", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosted_checkbox_bind_receives_a_bool()
    {
        var page = Page.Render(BoundIsland, Services());

        await page.On("input").Change("true");

        Assert.Contains("on: True", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hosted_multi_select_bind_receives_every_selected_value()
    {
        var page = Page.Render(BoundIsland, Services());

        await page.On("select").Raise("change", """{"type":"change","value":"red","values":["red","blue"]}""");

        Assert.Contains("picked: red,blue", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_handler_that_ignores_its_arguments_is_registered_without_a_payload()
    {
        var ignoring = new BlazorHandler(1, "click", typeof(EventArgs), BlazorValueKind.Text);
        var reading = new BlazorHandler(2, "click", typeof(Web.MouseEventArgs), BlazorValueKind.Text);

        var bare = ignoring.Bind(_ => Task.CompletedTask);
        var typed = reading.Bind(_ => Task.CompletedTask);

        Assert.IsType<Func<Task>>(bare);
        Assert.IsType<Func<Event, Task>>(typed);
    }

    [Fact]
    public void An_event_whose_argument_type_cannot_be_built_is_left_unwired()
    {
        var logs = new RecordingLogs();

        var page = Page.Render(CustomArgsIsland, Services(logs));
        page.Render();

        Assert.DoesNotContain("data-rask-on-click", page.Html, StringComparison.Ordinal);
        Assert.Contains("data-rask-on-focus", page.Html, StringComparison.Ordinal);
        var warning = Assert.Single(logs.Warnings);
        Assert.Contains("CustomArgsIsland", warning, StringComparison.Ordinal);
        Assert.Contains("onclick", warning, StringComparison.Ordinal);
        Assert.Contains(typeof(CustomArgs).FullName!, warning, StringComparison.Ordinal);
    }
}

/// <summary>One element per event, each handler writing what it was handed into the markup.</summary>
public sealed class EventBox : ComponentBase
{
    private string _seen = "nothing";

    private void See(FormattableString seen) => _seen = seen.ToString(CultureInfo.InvariantCulture);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "onkeydown", EventCallback.Factory.Create<Web.KeyboardEventArgs>(
            this, e => See($"{e.Type} {e.Key} {e.Code} shift {e.ShiftKey}")));
        builder.CloseElement();

        builder.OpenElement(2, "button");
        builder.AddAttribute(3, "onclick", EventCallback.Factory.Create<Web.MouseEventArgs>(
            this, e => See($"{e.Type} at {e.ClientX},{e.ClientY} button {e.Button} ctrl {e.CtrlKey} alt {e.AltKey}")));
        builder.CloseElement();

        builder.OpenElement(4, "section");
        builder.AddAttribute(5, "onwheel", EventCallback.Factory.Create<Web.WheelEventArgs>(
            this, e => See($"{e.Type} {e.DeltaX},{e.DeltaY} mode {e.DeltaMode} at {e.ClientX}")));
        builder.CloseElement();

        builder.OpenElement(6, "p");
        builder.AddContent(7, $"seen: {_seen}");
        builder.CloseElement();
    }
}

/// <summary>What <c>@bind</c> compiles to on a checkbox and on a <c>&lt;select multiple&gt;</c>.</summary>
public sealed class BoundBox : ComponentBase
{
    private bool _on;
    private string[] _picked = [];

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // The binder BEFORE the type, as `<input @bind="_on" type="checkbox">` emits it.
        builder.OpenElement(0, "input");
        builder.AddAttribute(1, "onchange", EventCallback.Factory.CreateBinder(this, v => _on = v, _on));
        builder.AddAttribute(2, "checked", _on);
        builder.AddAttribute(3, "type", "checkbox");
        builder.CloseElement();

        builder.OpenElement(4, "select");
        builder.AddAttribute(5, "multiple", true);
        builder.AddAttribute(6, "onchange", EventCallback.Factory.CreateBinder<string[]>(this, v => _picked = v, _picked));
        builder.CloseElement();

        builder.OpenElement(7, "p");
        builder.AddContent(8, $"on: {_on} picked: {string.Join(',', _picked)}");
        builder.CloseElement();
    }
}

/// <summary>A component library's own event args, which nothing here knows how to fill.</summary>
public sealed class CustomArgs : EventArgs;

/// <summary>One handler Rask cannot feed beside one it can.</summary>
public sealed class CustomArgsBox : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "onclick", EventCallback.Factory.Create<CustomArgs>(this, _ => { }));
        builder.AddAttribute(2, "onfocus", EventCallback.Factory.Create<Web.FocusEventArgs>(this, _ => { }));
        builder.CloseElement();
    }
}

public sealed partial class EventIsland : BlazorComponent<EventBox>;

public sealed partial class BoundIsland : BlazorComponent<BoundBox>;

public sealed partial class CustomArgsIsland : BlazorComponent<CustomArgsBox>;

/// <summary>Keeps every warning it is handed, rendered.</summary>
internal sealed class RecordingLogs : ILoggerProvider, ILogger
{
    public List<string> Warnings { get; } = [];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
        {
            lock (Warnings)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    public void Dispose()
    {
    }
}
