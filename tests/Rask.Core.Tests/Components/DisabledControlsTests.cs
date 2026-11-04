using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics;
using Rask.Core.Forms;
using Rask.Testing;
using static Rask.Core.Tests.Components.Refusals;
using Page = Rask.Testing.Page;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// Every handler a disabled form control registers is refused in Core (#1273): a browser sends no event to a disabled
// button, input, select or textarea — nor to one inside a <fieldset disabled>, outside its first legend — so what
// arrives for one was written by hand. Nothing runs, Rask.Forms says so once at Warning naming the control and never
// what it carried, and the markup is an enabled control's.
[Collection("ConsoleRedirect")]
public partial class DisabledControlsTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_disabled_button_refuses_a_click_sent_by_hand()
    {
        var clicks = 0;
        var page = Page.Render(() => Button.Name("save").Disabled(true).OnClick(() => clicks++)["Save"]);

        var reported = await Reported(() => page.Invoke(page.HandlerId("click")!));

        Assert.Equal(0, clicks);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Equal(RaskLogLevel.Warning, refused.Level);
        Assert.Contains("button 'save'", refused.Message, StringComparison.Ordinal);
        Assert.Contains("disabled", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_enabled_button_still_takes_a_click()
    {
        var clicks = 0;
        var page = Page.Render(() => Button.Disabled(false).OnClick(() => clicks++)["Save"]);

        var reported = await Reported(() => page.Invoke(page.HandlerId("click")!));

        Assert.Equal(1, clicks);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public void A_disabled_button_is_drawn_with_the_handler_an_enabled_one_has()
    {
        var enabled = Page.Render(() => Button.OnClick(() => { })["Save"]);

        var disabled = Page.Render(() => Button.Disabled(true).OnClick(() => { })["Save"]);

        Assert.Equal(enabled.HandlerId("click"), disabled.HandlerId("click"));
        Assert.Contains("disabled", disabled.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_button_disabled_through_its_attributes_refuses_a_click_too()
    {
        var clicks = 0;
        var page = Page.Render(() => Button.Attributes(("disabled", "")).OnClick(() => clicks++)["Save"]);

        var reported = await Reported(() => page.Invoke(page.HandlerId("click")!));

        Assert.Equal(0, clicks);
        Assert.Single(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_disabled_file_input_refuses_files_sent_by_hand()
    {
        var chosen = 0;
        var backend = new TestFileBackend();
        backend.Add("a.txt", "hello");
        var services = new ServiceCollection().AddSingleton<IBrowserFileBackend>(backend).BuildServiceProvider();
        var page = Page.Render(() => Input.Value<string>(null).Name("upload").Disabled(true).OnFiles(() => chosen++), services);

        var reported = await Reported(() => page.On("input").Files(backend));

        Assert.Equal(0, chosen);
        var refused = Assert.Single(reported, e => e.Category == "Rask.Forms");
        Assert.Contains("input 'upload'", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("a.txt", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_controlled_select_refuses_a_value_one_of_its_options_offers()
    {
        var heard = new List<string>();
        var page = Page.Render(() => Select.Value("a").Name("plan").Disabled(true).OnChange(heard.Add)[
            Option.Value("a")["A"], Option.Value("b")["B"]]);

        var reported = await Reported(() => page.Invoke(page.HandlerId("change")!, "{\"value\":\"b\"}"));

        Assert.Empty(heard);
        Assert.Contains("select 'plan'", Assert.Single(reported, e => e.Category == "Rask.Forms").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_button_inside_a_disabled_fieldset_refuses_a_click()
    {
        var clicks = 0;
        var page = Page.Render(() => Fieldset.Disabled(true)[Div[Button.OnClick(() => clicks++)["Save"]]]);

        var reported = await Reported(() => page.Invoke(page.HandlerId("click")!));

        Assert.Equal(0, clicks);
        Assert.Single(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task An_input_inside_a_disabled_fieldset_refuses_what_it_is_sent()
    {
        var heard = new List<string>();
        var page = Page.Render(() => Fieldset.Disabled(true)[Input.Value("kept").Name("code").OnChange(heard.Add)]);

        var reported = await Reported(() => page.Invoke(page.HandlerId("change")!, "{\"value\":\"forged\"}"));

        Assert.Empty(heard);
        Assert.Contains("input 'code'", Assert.Single(reported, e => e.Category == "Rask.Forms").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_button_in_the_first_legend_of_a_disabled_fieldset_still_takes_a_click()
    {
        var clicks = 0;
        var page = Page.Render(() => Fieldset.Disabled(true)[Legend[Button.OnClick(() => clicks++)["Expand"]]]);

        var reported = await Reported(() => page.Invoke(page.HandlerId("click")!));

        Assert.Equal(1, clicks);
        Assert.DoesNotContain(reported, e => e.Category == "Rask.Forms");
    }

    [Fact]
    public async Task A_button_after_a_disabled_fieldset_still_takes_a_click()
    {
        var clicks = 0;
        var page = Page.Render(() => Div[Fieldset.Disabled(true)[Span["off"]], Button.OnClick(() => clicks++)["Save"]]);

        await page.Invoke(page.HandlerId("click")!);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public async Task A_button_inside_an_enabled_fieldset_still_takes_a_click()
    {
        var clicks = 0;
        var page = Page.Render(() => Fieldset.Disabled(false)[Button.OnClick(() => clicks++)["Save"]]);

        await page.Invoke(page.HandlerId("click")!);

        Assert.Equal(1, clicks);
    }

    [Fact]
    public async Task A_component_left_clean_takes_a_click_again_once_its_fieldset_is_enabled()
    {
        var shell = new FieldsetShell();
        var page = Page.Render(shell);
        await Reported(() => page.Invoke(page.HandlerId("click")!));
        shell.Off = false;
        page.Render();

        await page.Invoke(page.HandlerId("click")!);

        Assert.Equal(1, shell.Counter.Clicks);
    }

    // A fieldset the page turns on and off around one component of its own, whose subtree stays clean between renders.
    private sealed class FieldsetShell : Component
    {
        public bool Off { get; set; } = true;

        internal ClickCounter Counter { get; } = new();

        protected override bool BypassRenderCache => true;

        protected override Component? Render() => Fieldset.Disabled(Off)[Counter];
    }

    private sealed class ClickCounter : Component
    {
        internal int Clicks { get; private set; }

        protected override Component? Render() => Button.OnClick(() => Clicks++)["Count"];
    }
}
