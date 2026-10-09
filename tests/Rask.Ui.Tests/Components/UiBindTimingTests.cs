using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>.Blur()</c> and <c>.Debounce(…)</c> on the kit's input and textarea — Flux's <c>wire:model.blur</c> and
///     <c>wire:model.live.debounce</c> — reach the element Core draws, with the rules written beside them.
/// </summary>
public partial class UiBindTimingTests : global::Rask.Core.RaskMarkup
{
    private sealed class Trip
    {
        public string Name { get; set; } = "";

        public string Notes { get; set; } = "";
    }

    private static IEnumerable<string> AtLeastThree(string value) =>
        value.Length < 3 ? ["Name is too short."] : [];

    [Fact]
    public void A_debounced_input_carries_its_pause_to_the_element()
    {
        var m = new Trip();

        var page = Page.Render(() => Form.Model(m)[
            Ui.Input.Bind(() => m.Name).Label("Name").Debounce(300.Milliseconds)
        ]);

        Assert.Equal("300", page.Find("input").Attribute("data-rask-debounce"));
        Assert.NotNull(page.Find("input").Attribute("data-rask-on-input"));
        Assert.Null(page.Find("input").Attribute("data-rask-on-change"));
    }

    [Fact]
    public void A_blur_bound_input_carries_its_mark_to_the_element()
    {
        var m = new Trip();

        var page = Page.Render(() => Form.Model(m)[Ui.Input.Bind(() => m.Name).Label("Name").Blur()]);

        Assert.Equal("blur", page.Find("input").Attribute("data-rask-bind-on"));
        Assert.Null(page.Find("input").Attribute("data-rask-on-input"));
    }

    [Fact]
    public void An_input_with_neither_step_stays_live()
    {
        var m = new Trip();

        var page = Page.Render(() => Form.Model(m)[Ui.Input.Bind(() => m.Name).Label("Name")]);

        Assert.Null(page.Find("input").Attribute("data-rask-debounce"));
        Assert.Null(page.Find("input").Attribute("data-rask-bind-on"));
        Assert.NotNull(page.Find("input").Attribute("data-rask-on-input"));
    }

    [Fact]
    public void A_textarea_carries_both_steps_to_the_element()
    {
        var m = new Trip();

        var debounced = Page.Render(() => Form.Model(m)[Ui.Textarea.Bind(() => m.Notes).Debounce(500.Milliseconds)]);
        var blurred = Page.Render(() => Form.Model(m)[Ui.Textarea.Bind(() => m.Notes).Blur()]);

        Assert.Equal("500", debounced.Find("textarea").Attribute("data-rask-debounce"));
        Assert.Equal("blur", blurred.Find("textarea").Attribute("data-rask-bind-on"));
    }

    [Fact]
    public async Task A_blur_bound_inputs_message_shows_under_it_in_the_one_message_leaving_sends()
    {
        var m = new Trip();
        var page = Page.Render(() => Form.Model(m)[
            Ui.Input.Bind(() => m.Name).Label("Name").Blur().Validate(AtLeastThree)
        ]);

        await page.On("input").Change("At");

        page.Shows("Name is too short.");
        Assert.NotNull(page.Find("input").Attribute("data-rask-on-edit"));
    }

    [Fact]
    public async Task The_first_edit_takes_the_message_from_under_the_input()
    {
        var m = new Trip();
        var page = Page.Render(() => Form.Model(m)[
            Ui.Input.Bind(() => m.Name).Label("Name").Debounce(300.Milliseconds).Validate(AtLeastThree)
        ]);
        await page.On("input").Input("At");

        await page.On("input").Raise("edit");

        page.DoesNotShow("Name is too short.");
        Assert.Null(page.Find("input").Attribute("data-rask-on-edit"));
    }

    [Fact]
    public async Task A_masked_input_that_waits_still_lays_the_value_into_its_pattern()
    {
        var m = new Trip();
        var page = Page.Render(() => Form.Model(m)[
            Ui.Input.Bind(() => m.Name).Label("Phone").Mask("(999) 999-9999").Debounce(300.Milliseconds)
        ]);

        await page.On("input").Input("5551234567");

        Assert.Equal("(555) 123-4567", m.Name);
        Assert.Equal("(999) 999-9999", page.Find("input").Attribute("data-rask-mask"));
    }

    [Fact]
    public async Task Two_rules_on_a_kit_input_run_in_order_and_stop_at_the_first_that_rejects()
    {
        var m = new Trip();
        var looked = new List<string>();
        var page = Page.Render(() => Form.Model(m)[
            Ui.Input.Bind(() => m.Name).Label("Name").Debounce(300.Milliseconds)
                .Validate(AtLeastThree)
                .Validate(async v =>
                {
                    await Task.Yield();
                    looked.Add(v);
                    return v == "Atlantis" ? ["Name is taken."] : [];
                })
        ]);

        await page.On("input").Input("At");
        await page.On("input").Input("Atlantis");

        Assert.Equal(["Atlantis"], looked);
        page.Shows("Name is taken.");
    }
}
