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
    public void A_handler_that_forwards_a_callback_keeps_the_value_task_it_returns()
    {
        var inner = new Callback(() => { });

        var button = Button.OnClick(() => inner.Invoke());

        Assert.IsType<Func<ValueTask>>(button.OnClick.Handler);
    }
}
