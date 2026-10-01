namespace Rask.Core.Tests.Builder;

// The steps that write a common value without its ceremony: `.Disabled()` for `.Disabled(true)`,
// `.Class("p-4", wide ? "w-full" : null)` instead of concatenating, and a handler that forwards another
// callback — `() => OnRate.Invoke(i)` — kept as the ValueTask it returns rather than dropped as an Action.
public partial class ShorthandStepTests : RaskMarkup
{
    [Fact]
    public void A_flag_step_without_an_argument_turns_it_on()
    {
        var button = Button.Disabled()["Save"];

        var html = button.ToHtml();

        Assert.Contains("disabled", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_flag_step_with_an_argument_still_takes_the_value()
    {
        var saving = false;

        var html = Button.Disabled(saving)["Save"].ToHtml();

        Assert.DoesNotContain("disabled", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Class_joins_its_parts_with_one_space_and_skips_empty_ones()
    {
        var wide = true;

        var html = Div.Class(" p-4 rounded", wide ? "w-full" : "w-1/2", null, "  ").ToHtml();

        Assert.Equal("<div class=\"p-4 rounded w-full\"></div>", html);
    }

    [Fact]
    public void Class_with_nothing_left_writes_no_attribute()
    {
        string? none = null;

        var html = Div.Class(none, " ").ToHtml();

        Assert.Equal("<div></div>", html);
    }

    [Fact]
    public async Task A_handler_that_forwards_a_callback_is_awaited()
    {
        var gate = new TaskCompletionSource();
        var inner = new Callback(async () => await gate.Task);
        var button = Button.OnClick(() => inner.Invoke());

        var pending = button.OnClick.Invoke(null!);
        var waitedBeforeRelease = !pending.IsCompleted;
        gate.SetResult();
        await pending;

        Assert.True(waitedBeforeRelease);
    }

    // The runtime runs a handler by its shape — the DOM dispatch, the frame check — and a shape it did not know was
    // a click that did nothing. So a ValueTask handler is held as the Task-returning one it already dispatches.
    [Fact]
    public void A_forwarding_handler_is_held_in_a_shape_the_runtime_dispatches()
    {
        var inner = new Callback(() => { });

        var plain = Button.OnClick(() => inner.Invoke());
        var withEvent = Button.OnClick(e => inner.Invoke());

        Assert.IsType<Func<Task>>(plain.OnClick.Handler);
        Assert.IsType<Func<PointerEvent, Task>>(withEvent.OnClick.Handler);
    }

    [Fact]
    public void A_forwarding_handler_still_belongs_to_the_component_that_wrote_it()
    {
        var host = ForwardingHost;

        var button = host.Star(3);

        Assert.Same(host, DelegateOwner.Resolve(button.OnClick.Handler));
    }
}

internal sealed partial class ForwardingHost : Component
{
    public Callback<int> OnRate { get; set; }

    internal Element Star(int n) => Button.OnClick(() => OnRate.Invoke(n));

    protected override Component? Render() => Star(1);
}
