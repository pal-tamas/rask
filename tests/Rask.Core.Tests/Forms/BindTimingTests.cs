using Rask.Core.Forms;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Forms;

// When a bound control speaks: by default at the next action, and what `.Live()`, `.Debounce(…)` and `.Blur()`
// make of that — which handler the control renders, the marks the runtime reads beside it, and what that
// handler does when the value arrives. Plain Core controls here; the kit's forward to them (UiBindTimingTests).
public partial class BindTimingTests : global::Rask.Core.RaskMarkup
{
    private static IEnumerable<string> AtLeastThree(string value) =>
        value.Length < 3 ? ["too short"] : [];

    [Fact]
    public void A_bound_input_waits_for_the_next_action_unless_it_says_otherwise()
    {
        var m = new Draft();

        var page = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name)]);

        Assert.NotNull(page.HandlerId("change"));
        Assert.Equal("action", page.Attr("data-rask-bind-on"));
        Assert.Null(page.HandlerId("input"));
        Assert.Null(page.Attr("data-rask-debounce"));
    }

    [Fact]
    public void A_bound_number_and_a_bound_textarea_wait_for_the_next_action_too()
    {
        var m = new Draft();

        var number = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Age)]);
        var textarea = Page.Render(() => Form.Model(m)[Textarea.Bind(() => m.Name)]);

        Assert.Equal("action", number.Attr("data-rask-bind-on"));
        Assert.Null(number.HandlerId("input"));
        Assert.Equal("action", textarea.Attr("data-rask-bind-on"));
        Assert.Null(textarea.HandlerId("input"));
    }

    [Fact]
    public void A_live_field_is_sent_as_it_is_typed_after_a_pause_of_150_milliseconds()
    {
        var m = new Draft();

        var text = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Live()]);
        var number = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Age).Live()]);
        var textarea = Page.Render(() => Form.Model(m)[Textarea.Bind(() => m.Name).Live()]);

        Assert.All([text, number, textarea], page =>
        {
            Assert.NotNull(page.HandlerId("input"));
            Assert.Equal("150", page.Attr("data-rask-debounce"));
            Assert.Null(page.HandlerId("change"));
            Assert.Null(page.Attr("data-rask-bind-on"));
        });
    }

    [Fact]
    public void Live_names_no_pause_of_its_own_where_the_chain_already_says_when()
    {
        var m = new Draft();

        var ownPause = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Live().Debounce(300.Milliseconds)]);
        var pauseFirst = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Debounce(300.Milliseconds).Live()]);
        var blurFirst = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Blur().Live()]);

        Assert.Equal("300", ownPause.Attr("data-rask-debounce"));
        Assert.Equal("300", pauseFirst.Attr("data-rask-debounce"));
        Assert.Equal("blur", blurFirst.Attr("data-rask-bind-on"));
        Assert.Null(blurFirst.Attr("data-rask-debounce"));
    }

    [Fact]
    public void A_pause_of_zero_sends_every_keystroke_and_never_means_wait_for_the_action()
    {
        var m = new Draft();

        var text = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Debounce(TimeSpan.Zero)]);
        var live = Page.Render(() => Form.Model(m)[Textarea.Bind(() => m.Name).Live().Debounce(TimeSpan.Zero)]);
        var number = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Age).Debounce(TimeSpan.Zero)]);

        Assert.All([text, live, number], page =>
        {
            Assert.NotNull(page.HandlerId("input"));
            Assert.Null(page.Attr("data-rask-debounce"));
            Assert.Null(page.Attr("data-rask-bind-on"));
        });
        Assert.NotNull(text.HandlerId("change"));
        Assert.Null(number.HandlerId("change"));
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
    [InlineData(InputType.Color)]
    public void A_control_that_is_chosen_waits_for_the_next_action_and_counts_no_pause(InputType type)
    {
        var m = new Draft();

        var plain = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Type(type)]);
        var live = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Name).Type(type).Live()]);

        Assert.NotNull(plain.HandlerId("change"));
        Assert.Equal("action", plain.Attr("data-rask-bind-on"));
        Assert.Null(plain.HandlerId("input"));
        Assert.NotNull(live.HandlerId("change"));
        Assert.Null(live.Attr("data-rask-bind-on"));
        Assert.Null(live.Attr("data-rask-debounce"));
    }

    [Fact]
    public void A_bound_checkbox_waits_for_the_next_action_and_a_live_one_is_sent_as_it_is_ticked()
    {
        var m = new Draft();

        var plain = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Agreed)]);
        var live = Page.Render(() => Form.Model(m)[Input.Bind(() => m.Agreed).Live()]);

        Assert.Equal("action", plain.Attr("data-rask-bind-on"));
        Assert.NotNull(plain.HandlerId("change"));
        Assert.Null(live.Attr("data-rask-bind-on"));
        Assert.NotNull(live.HandlerId("change"));
    }

    [Fact]
    public void A_bound_select_waits_for_the_next_action_and_a_live_one_is_sent_as_it_is_picked()
    {
        var m = new Draft();

        var plain = Page.Render(() => Form.Model(m)[Select.Bind(() => m.Name)[Option.Value("a")["A"]]]);
        var live = Page.Render(() => Form.Model(m)[Select.Bind(() => m.Name).Live()[Option.Value("a")["A"]]]);
        var many = Page.Render(() => Form.Model(m)[Select.Bind(() => m.Tags).Multiple()[Option.Value("a")["A"]]]);

        Assert.Equal("action", plain.Attr("data-rask-bind-on"));
        Assert.NotNull(plain.HandlerId("change"));
        Assert.Null(live.Attr("data-rask-bind-on"));
        Assert.NotNull(live.HandlerId("change"));
        Assert.Equal("action", many.Attr("data-rask-bind-on"));
    }

    [Fact]
    public async Task A_control_that_waited_binds_validates_and_runs_AfterBind_when_its_value_arrives()
    {
        var m = new Draft();
        var heard = new List<string>();
        EditContext? ctx = null;
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Validate(AtLeastThree).AfterBind(heard.Add),
            Test.EditContextProbe(c => ctx = c)
        ]);

        await page.On("input").Change("ab");

        Assert.Equal("ab", m.Name);
        Assert.Equal(["ab"], heard);
        Assert.Equal(["too short"], ctx!.GetValidationMessages(new FieldIdentifier(m, nameof(Draft.Name))));
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
    public async Task A_step_left_out_of_the_next_render_makes_the_field_wait_for_the_action_again()
    {
        var m = new Draft();
        var waits = true;
        var page = Page.Render(() => Form.Model(m)[
            waits ? Input.Bind(() => m.Name).Debounce(300.Milliseconds) : Input.Bind(() => m.Name),
            Button.OnClick(() => waits = false)["plain"]
        ]);

        await page.On("button").Click();

        Assert.Null(page.Attr("data-rask-debounce"));
        Assert.Equal("action", page.Attr("data-rask-bind-on"));
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
    public async Task A_field_that_waited_for_the_action_asks_to_hear_the_first_edit_of_a_refused_value()
    {
        var m = new Draft();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Validate(AtLeastThree)
        ]);

        await page.On("input").Change("ab");

        Assert.NotNull(page.HandlerId("edit"));
    }

    [Fact]
    public async Task A_field_sent_at_every_keystroke_never_asks_to_hear_the_first_edit()
    {
        var m = new Draft();
        var page = Page.Render(() => Form.Model(m)[
            Input.Bind(() => m.Name).Debounce(TimeSpan.Zero).Validate(AtLeastThree)
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

        public bool Agreed { get; set; }

        public List<string> Tags { get; set; } = [];
    }
}
