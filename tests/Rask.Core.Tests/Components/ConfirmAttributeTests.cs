using Rask.Core.Live;

#pragma warning disable RASK014 // a test-defined host is constructed to be rendered live, as BuilderResetTests does

namespace Rask.Core.Tests.Components;

/// <summary>
///     <c>.Confirm("…")</c> — Livewire's <c>wire:confirm</c> — as the attributes the runtime reads before a click or a
///     submit leaves the browser (rask-confirm.ts; the dialogs themselves are <c>ConfirmClientTests</c>).
/// </summary>
public partial class ConfirmAttributeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_question_renders_as_data_rask_confirm()
    {
        var button = Button.Confirm("Delete the partner?")["Delete"];

        var html = button.ToHtml();

        Assert.Equal("<button data-rask-confirm=\"Delete the partner?\">Delete</button>", html);
    }

    [Fact]
    public void The_prompt_form_writes_the_answer_it_waits_for_beside_the_question()
    {
        var button = Button.Confirm("Type the name to delete it", expect: "Acme & Co")["Delete"];

        var html = button.ToHtml();

        Assert.Equal(
            "<button data-rask-confirm=\"Type the name to delete it\" data-rask-confirm-expect=\"Acme &amp; Co\">Delete</button>",
            html);
    }

    [Fact]
    public void A_form_asks_before_its_submit()
    {
        var html = Page.Render(() => Form.Model(new ConfirmOrder()).OnSubmit(() => { }).Confirm("Send the order?")).Html;

        Assert.Contains("data-rask-confirm=\"Send the order?\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_sits_in_the_data_group_ahead_of_the_handler_it_guards()
    {
        var host = new ConfirmedClickHost();

        var html = Render(host);

        Assert.True(
            html.IndexOf("data-rask-confirm=", StringComparison.Ordinal)
            < html.IndexOf("data-rask-on-click=", StringComparison.Ordinal),
            html);
        Assert.True(
            html.IndexOf("data-rask-confirm=", StringComparison.Ordinal) > html.IndexOf("data-x=", StringComparison.Ordinal),
            html);
    }

    [Fact]
    public void A_render_that_stops_asking_drops_the_question()
    {
        var host = new ConfirmedClickHost();
        Render(host);

        host.Asks = false;
        var html = Render(host);

        Assert.DoesNotContain("data-rask-confirm", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bare_entry_after_one_that_asked_carries_no_question_whatever_other_steps_it_had()
    {
        var host = new ConfirmedBareHost();
        Render(host);

        host.Asks = false;
        var html = Render(host);

        Assert.Equal("<div></div>", html);
    }

    [Fact]
    public void A_question_dropped_beside_an_aria_step_goes_with_the_rest_of_the_reset()
    {
        var host = new ConfirmedAriaHost();
        Render(host);

        host.Asks = false;
        var html = Render(host);

        Assert.DoesNotContain("data-rask-confirm", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"x\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_render_that_asks_something_else_writes_the_new_question()
    {
        var host = new ConfirmedClickHost();
        Render(host);

        host.Question = "Really sure?";
        var html = Render(host);

        Assert.Contains("data-rask-confirm=\"Really sure?\"", html, StringComparison.Ordinal);
    }

    private static string Render(Component host)
    {
        var sp = RenderHarness.EmptyServices();
        using var ctx = LiveRenderContext.Begin(host, sp);
        var resolved = ctx.GetOrCreate(_ => host);
        LiveRenderContext.NotifyParameters(resolved, propsChanged: true);
        return resolved.ToHtml();
    }
}

internal sealed class ConfirmOrder
{
    public string? Note { get; set; }
}

internal sealed partial class ConfirmedBareHost : Component
{
    internal bool Asks = true;

    protected override Component? Render() => Asks ? Div.Confirm("Sure?") : Div;
}

internal sealed partial class ConfirmedAriaHost : Component
{
    internal bool Asks = true;

    protected override Component? Render() =>
        Asks ? Div.AriaLabel("x").Confirm("Sure?") : Div.AriaLabel("x");
}

internal sealed partial class ConfirmedClickHost : Component
{
    internal bool Asks = true;
    internal string Question = "Sure?";

    protected override Component? Render() =>
        Asks
            ? Button.Data("x", "1").OnClick(() => { }).Confirm(Question)["Go"]
            : Button.Data("x", "1").OnClick(() => { })["Go"];
}
