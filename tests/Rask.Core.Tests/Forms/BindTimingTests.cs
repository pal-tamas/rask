using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// `.Blur()` and `.Debounce(…)` on a bound Input or Textarea: which handler the field renders, the marks the
// runtime reads beside it, and what that handler does when the value arrives.
public partial class BindTimingTests : global::Rask.Core.RaskMarkup
{
    private static IEnumerable<string> AtLeastThree(string value) =>
        value.Length < 3 ? ["too short"] : [];

    [Fact]
    public void A_live_string_input_renders_as_it_always_did()
    {
        var m = new Draft();

        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name)]);

        Assert.NotNull(page.HandlerId("input"));
        Assert.NotNull(page.HandlerId("change"));
        Assert.Null(page.Attr("data-rask-debounce"));
        Assert.Null(page.Attr("data-rask-bind-on"));
    }

    [Fact]
    public void A_debounced_input_renders_its_pause_beside_one_input_handler()
    {
        var m = new Draft();

        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Debounce(300.Milliseconds)]);

        Assert.NotNull(page.HandlerId("input"));
        Assert.Equal("300", page.Attr("data-rask-debounce"));
        Assert.Null(page.HandlerId("change"));
        Assert.Null(page.Attr("data-rask-bind-on"));
    }

    [Fact]
    public void A_blur_bound_input_renders_one_change_handler_and_says_it_is_typed_into()
    {
        var m = new Draft();

        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Blur()]);

        Assert.NotNull(page.HandlerId("change"));
        Assert.Equal("blur", page.Attr("data-rask-bind-on"));
        Assert.Null(page.HandlerId("input"));
        Assert.Null(page.Attr("data-rask-debounce"));
    }

    [Fact]
    public void A_number_input_takes_both_steps_too()
    {
        var m = new Draft();

        var debounced = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Age).Debounce(1.Second)]);
        var blurred = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Age).Blur()]);

        Assert.Equal("1000", debounced.Attr("data-rask-debounce"));
        Assert.NotNull(debounced.HandlerId("input"));
        Assert.Null(debounced.HandlerId("change"));
        Assert.Equal("blur", blurred.Attr("data-rask-bind-on"));
        Assert.Null(blurred.HandlerId("input"));
    }

    [Fact]
    public void A_textarea_takes_both_steps()
    {
        var m = new Draft();

        var debounced = Page.Render(() => Form.Model(m)[Textarea.Bind(() => m.Name).Debounce(250.Milliseconds)]);
        var blurred = Page.Render(() => Form.Model(m)[Textarea.Bind(() => m.Name).Blur()]);

        Assert.Equal("250", debounced.Attr("data-rask-debounce"));
        Assert.Null(debounced.HandlerId("change"));
        Assert.Equal("blur", blurred.Attr("data-rask-bind-on"));
        Assert.Null(blurred.HandlerId("input"));
    }

    [Theory]
    [InlineData(InputType.Checkbox)]
    [InlineData(InputType.Radio)]
    [InlineData(InputType.Range)]
    [InlineData(InputType.File)]
    [InlineData(InputType.Color)]
    public void A_control_that_is_chosen_rather_than_typed_into_ignores_both_steps(InputType type)
    {
        var m = new Draft();

        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Type(type).Debounce(300.Milliseconds).Blur()
        ]);

        Assert.Null(page.Attr("data-rask-debounce"));
        Assert.Null(page.Attr("data-rask-bind-on"));
        Assert.NotNull(page.HandlerId("change"));
    }

    [Fact]
    public void The_later_of_the_two_steps_is_the_one_that_holds()
    {
        var m = new Draft();

        var blurLast = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Debounce(300.Milliseconds).Blur()]);
        var debounceLast = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Blur().Debounce(300.Milliseconds)]);

        Assert.Equal("blur", blurLast.Attr("data-rask-bind-on"));
        Assert.Null(blurLast.Attr("data-rask-debounce"));
        Assert.Equal("300", debounceLast.Attr("data-rask-debounce"));
        Assert.Null(debounceLast.Attr("data-rask-bind-on"));
    }

    [Fact]
    public async Task A_step_left_out_of_the_next_render_makes_the_field_live_again()
    {
        var m = new Draft();
        var waits = true;
        var page = Page.Render(() => Form.Model(m)[
            waits ? Input.Bind(() => m.Name).Debounce(300.Milliseconds) : Input.Bind(() => m.Name),
            Button.OnClick(() => waits = false)["live"]
        ]);

        await page.On("button").Click();

        Assert.Null(page.Attr("data-rask-debounce"));
        Assert.NotNull(page.HandlerId("change"));
    }

    [Fact]
    public async Task A_debounced_field_binds_and_validates_in_the_one_message_the_pause_sends()
    {
        var m = new Draft();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Debounce(300.Milliseconds).Validate(AtLeastThree),
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("input").Input("ab");

        Assert.Equal("ab", m.Name);
        Assert.Equal(["too short"], ctx!.GetValidationMessages(new FieldIdentifier(m, nameof(Draft.Name))));
    }

    [Fact]
    public async Task A_blur_bound_field_binds_and_validates_in_the_one_message_leaving_it_sends()
    {
        var m = new Draft();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Blur().Validate(AtLeastThree),
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("input").Change("ab");

        Assert.Equal("ab", m.Name);
        Assert.Equal(["too short"], ctx!.GetValidationMessages(new FieldIdentifier(m, nameof(Draft.Name))));
    }

    [Fact]
    public async Task AfterBind_runs_with_the_value_a_waiting_field_wrote()
    {
        var m = new Draft();
        var heard = new List<string>();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Blur().AfterBind(heard.Add)
        ]);

        await page.On("input").Change("Atlantis");

        Assert.Equal(["Atlantis"], heard);
    }

    [Fact]
    public async Task A_field_with_a_message_asks_to_hear_the_first_edit_and_a_valid_one_does_not()
    {
        var m = new Draft();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Debounce(300.Milliseconds).Validate(AtLeastThree)
        ]);
        var whileValid = page.HandlerId("edit");

        await page.On("input").Input("ab");

        Assert.Null(whileValid);
        Assert.NotNull(page.HandlerId("edit"));
    }

    [Fact]
    public async Task The_first_edit_takes_the_message_away_without_running_a_rule()
    {
        var m = new Draft();
        var asked = 0;
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Blur().Validate(v =>
            {
                asked++;
                return AtLeastThree(v);
            }),
            Test.EditContextProbe(c => ctx = c)
        ]);
        await page.On("input").Change("ab");
        var askedBefore = asked;

        await page.Invoke(page.HandlerId("edit")!, """{"type":"edit"}""");

        Assert.Empty(ctx!.GetValidationMessages(new FieldIdentifier(m, nameof(Draft.Name))));
        Assert.Null(page.HandlerId("edit"));
        Assert.Equal(askedBefore, asked);
        Assert.Equal("ab", m.Name);
    }

    [Fact]
    public async Task A_live_field_never_asks_to_hear_the_first_edit()
    {
        var m = new Draft();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Validate(AtLeastThree)
        ]);

        await page.On("input").Change("ab");

        Assert.Null(page.HandlerId("edit"));
    }

    // Save pressed while a lookup is still running: the submit starts the rules again and waits for them, so
    // the form is never saved past a check that has not answered.
    [Fact]
    public async Task A_submit_waits_for_a_rule_that_was_still_running_when_it_was_pressed()
    {
        var m = new Draft();
        var saved = 0;
        var lookups = new List<TaskCompletionSource<IEnumerable<string>>>();
        var page = Page.Render(() => Form.Model(m).OnSubmit(_ => saved++)[
            Input.Bind(() => m.Name).Debounce(300.Milliseconds).Validate(async _ =>
            {
                var lookup = new TaskCompletionSource<IEnumerable<string>>();
                lookups.Add(lookup);
                return await lookup.Task;
            })
        ]);
        var paused = page.Invoke(page.HandlerId("input")!, """{"type":"input","value":"Atlantis"}""");
        var pressed = page.On("form").Submit("""{"form":{"Name":"Atlantis"}}""");

        lookups[0].TrySetResult([]);
        await paused;
        var savedBeforeTheSubmitsOwnLookup = saved;
        lookups[1].TrySetResult(["Name is taken."]);
        await pressed;

        Assert.Equal(0, savedBeforeTheSubmitsOwnLookup);
        Assert.Equal(0, saved);
        Assert.Equal(2, lookups.Count);
    }

    private sealed class Draft
    {
        public string Name { get; set; } = "";

        public int Age { get; set; }
    }
}
