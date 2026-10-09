using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Messaging;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's toast: where <c>Toast.Success("Saved")</c> appears, and what one looks like once it has.
/// </summary>
/// <remarks>
///     <para>
///     A toast is raised through the <c>Toast</c> facade and drawn by <c>Ui.Toast</c> in the layout, so most of
///     what is pinned here is the markup the rest stands on: the popover and live region, the variant Flux's
///     icons key on, and the three hooks the Rask runtime reads — which is how a kit that ships no script
///     still counts a toast down, waits while it is being read, and closes it.
///     </para>
///     <para>
///     What only a browser shows — the look, to the pixel — is <c>scripts/flux/parity-toast.mjs</c>'s.
///     </para>
/// </remarks>
public partial class UiToastTests : global::Rask.Core.RaskMarkup
{
    private static ToastMessage Say(string text, ToastLevel level = ToastLevel.Info, int id = 0) => new(id, level, text);

    private static string Draw(ToastOptions options, params ToastMessage[] messages) =>
        UiToast.Messages(messages, static _ => { }, options).ToHtml();

    private static string One(ToastMessage message) => Draw(new ToastOptions(), message);

    // A stack, as Ui.ToastGroup[Ui.Toast] draws it.
    private static string Stack(bool expanded, params ToastMessage[] messages) =>
        UiToast.Draw(messages, static _ => { }, new UiToastLook(
            Ui.ToastPosition.BottomEnd, false, null, UiToast.DefaultDuration, new UiToastStack(null, expanded, null))).ToHtml();

    [Fact]
    public void A_toast_is_a_manual_popover_the_runtime_shows_and_a_polite_live_region()
    {
        var html = One(Say("Saved"));

        Assert.Contains("popover=\"manual\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-popover-open=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-atomic=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-toast ", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-toast-dialog", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ToastLevel.Info, "")]
    [InlineData(ToastLevel.Success, "success")]
    [InlineData(ToastLevel.Warning, "warning")]
    [InlineData(ToastLevel.Error, "danger")]
    public void Each_level_is_one_of_Fluxs_variants(ToastLevel level, string variant)
    {
        var html = One(Say("Saved", level));

        Assert.Contains($"data-variant=\"{variant}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_variant_draws_its_own_icon_and_a_plain_toast_draws_none()
    {
        var plain = One(Say("Saved"));
        var success = One(Say("Saved", ToastLevel.Success));
        var danger = One(Say("Saved", ToastLevel.Error));

        Assert.DoesNotContain("viewBox=\"0 0 16 16\"", plain, StringComparison.Ordinal);
        Assert.Contains("text-lime-600", success, StringComparison.Ordinal);
        Assert.Contains("text-rose-500", danger, StringComparison.Ordinal);
        Assert.NotEqual(Shape(success), Shape(danger));
    }

    [Fact]
    public void Text_alone_reads_as_the_heading_and_under_a_heading_as_its_detail()
    {
        var alone = One(Say("Saved"));
        var headed = One(Say("You can always update this in your settings.") with { Title = "Changes saved" });

        Assert.Contains("font-medium text-zinc-800 dark:text-white group-data-invert/toast:text-white dark:group-data-invert/toast:text-zinc-800\">Saved", alone, StringComparison.Ordinal);
        Assert.Contains(">Changes saved</div>", headed, StringComparison.Ordinal);
        Assert.Contains("text-zinc-500", headed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.ToastPosition.BottomEnd, "bottom end", "m-6 ms-auto mt-auto")]
    [InlineData(Ui.ToastPosition.BottomCenter, "bottom center", "m-6 mx-auto mt-auto")]
    [InlineData(Ui.ToastPosition.BottomStart, "bottom start", "m-6 me-auto mt-auto")]
    [InlineData(Ui.ToastPosition.TopEnd, "top end", "m-6 ms-auto mb-auto")]
    [InlineData(Ui.ToastPosition.TopCenter, "top center", "m-6 mx-auto mb-auto")]
    [InlineData(Ui.ToastPosition.TopStart, "top start", "m-6 me-auto mb-auto")]
    public void Each_position_is_a_corner_of_the_viewport(Ui.ToastPosition position, string name, string margins)
    {
        var html = Draw(new ToastOptions().At(position), Say("Saved"));

        Assert.Contains($"data-position=\"{name}\"", html, StringComparison.Ordinal);
        Assert.Contains(margins, html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_toast_counts_down_in_the_browser_for_its_duration_and_the_fade_after_it()
    {
        var byDefault = One(Say("Saved"));

        var asked = One(Say("Saved") with { Duration = TimeSpan.FromSeconds(1) });

        Assert.Contains("data-rask-dismiss-after=\"5350\"", byDefault, StringComparison.Ordinal);
        Assert.Contains("--ui-toast-duration:5000ms", byDefault, StringComparison.Ordinal);
        Assert.Contains("data-rask-dismiss-after=\"1350\"", asked, StringComparison.Ordinal);
    }

    [Fact]
    public void The_apps_own_default_duration_is_the_one_a_toast_without_its_own_takes()
    {
        var html = Draw(new ToastOptions().For(TimeSpan.FromSeconds(8)), Say("Saved"));

        Assert.Contains("data-rask-dismiss-after=\"8350\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_permanent_toast_carries_no_countdown()
    {
        var html = One(Say("Saved") with { Duration = Timeout.InfiniteTimeSpan });

        Assert.DoesNotContain("data-rask-dismiss-after", html, StringComparison.Ordinal);
        Assert.DoesNotContain("--ui-toast-duration", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_close_button_is_what_the_runtime_and_Escape_press()
    {
        var html = One(Say("Saved"));

        Assert.Contains("data-rask-dismiss", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-shortcut=\"escape\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_action_is_a_button_that_shows_it_is_working_and_a_link_action_is_a_real_link()
    {
        var button = One(Say("Changes saved.") with { Action = new ToastAction("Undo", new Callback(() => { })) });

        var link = One(Say("Invoice created.") with { Action = new ToastAction("View", default) { Href = "/invoices/1" } });

        Assert.Contains("data-ui-toast-action-button", button, StringComparison.Ordinal);
        Assert.Contains("data-ui-loading-indicator", button, StringComparison.Ordinal);
        Assert.Contains(">Undo</span>", button, StringComparison.Ordinal);
        Assert.Contains("data-ui-toast-action-link", link, StringComparison.Ordinal);
        Assert.Contains("href=\"/invoices/1\"", link, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_sits_under_the_text()
    {
        var html = One(Say("Invoice created.") with { Link = new ToastLink("View invoice", "/invoices/1") });

        Assert.Matches("Invoice created\\.</div><a [^>]*href=\"/invoices/1\"[^>]*>View invoice</a>", html);
    }

    [Fact]
    public void Without_a_group_the_newest_toast_takes_the_place_of_the_one_showing()
    {
        var html = Draw(new ToastOptions(), Say("First", id: 1), Say("Second", id: 2));

        Assert.DoesNotContain("First", html, StringComparison.Ordinal);
        Assert.Contains("Second", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "data-ui-toast-dialog"));
    }

    [Fact]
    public void A_group_stacks_newest_first_and_tells_each_toast_where_it_is()
    {
        var html = Stack(expanded: false, Say("First", id: 1), Say("Second", id: 2), Say("Third", id: 3));

        Assert.Contains("data-ui-toast-group", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("Third", StringComparison.Ordinal) < html.IndexOf("First", StringComparison.Ordinal));
        Assert.Contains("--ui-toast-index:0;anchor-name:--ui-toast-front,--ui-toast-3;", html, StringComparison.Ordinal);
        Assert.Contains("--ui-toast-index:1;anchor-name:--ui-toast-2;position-anchor:--ui-toast-3;", html, StringComparison.Ordinal);
        Assert.Contains("--ui-toast-index:2;anchor-name:--ui-toast-1;position-anchor:--ui-toast-2;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_pointer_holds_a_countdown_and_a_permanent_toast_has_none_to_hold()
    {
        var timed = One(Say("Saved"));

        var permanent = One(Say("Saved") with { Duration = Timeout.InfiniteTimeSpan });

        // Measured on Flux: a toast whose close button has focus still goes on time.
        Assert.Contains("data-rask-dismiss-after=\"5350\" data-rask-dismiss-hold=\"pointer\"", timed, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-dismiss-hold", permanent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_is_the_scope_one_pointer_holds_every_countdown_in_and_a_lone_toast_is_none()
    {
        var stack = Stack(expanded: false, Say("First", id: 1), Say("Second", id: 2));

        var alone = One(Say("Saved"));

        Assert.Matches("<div [^>]*data-ui-toast-group data-position=\"bottom end\" data-rask-popover-open=\"true\" data-rask-dismiss-scope role=\"status\">", stack);
        Assert.Equal(2, Regex.Matches(stack, "data-rask-dismiss-hold=\"pointer\"").Count);
        Assert.DoesNotContain("data-rask-dismiss-scope", alone, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fade_waits_with_the_countdown_for_every_toast_of_a_hovered_stack()
    {
        var css = UiStylesheet.Css;

        var paused = css.IndexOf("[data-ui-toast-group]:hover>[data-ui-toast-dialog]>:first-child{animation-play-state:paused!important}", StringComparison.Ordinal);

        // Without it a toast the runtime is holding would fade out on time and stay, invisible.
        Assert.True(paused > 0, "a hovered stack does not pause the fade of the toasts the pointer is not on");
        Assert.Contains("[data-ui-toast-group]:hover>[data-ui-toast-dialog],", css[(paused - 200)..paused], StringComparison.Ordinal);
    }

    [Fact]
    public void A_stack_is_not_taken_down_by_Escape_and_opens_for_good_when_expanded()
    {
        var deck = Stack(expanded: false, Say("First", id: 1));

        var open = Stack(expanded: true, Say("First", id: 1));

        Assert.DoesNotContain("data-rask-shortcut", deck, StringComparison.Ordinal);
        Assert.DoesNotContain("data-expanded", deck, StringComparison.Ordinal);
        Assert.Contains("data-expanded", open, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_toast_raised_through_the_facade_shows_in_the_Ui_Toast_the_layout_places()
    {
        var page = Page.Render(
            () => [Button.OnClick(() => Toast.Success("Saved").Heading("Order 42"))["Save"], Ui.Toast.TopEnd.Invert()],
            Session());
        Assert.DoesNotContain("data-ui-toast-dialog", page.Html, StringComparison.Ordinal);

        await page.Click("Save");

        Assert.Contains("data-position=\"top end\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("data-variant=\"success\"", page.Html, StringComparison.Ordinal);
        Assert.Contains(" data-invert", page.Html, StringComparison.Ordinal);
        Assert.Contains(">Order 42</div>", page.Html, StringComparison.Ordinal);
        Assert.Contains(">Saved</div>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closing_a_toast_takes_it_off_the_page()
    {
        var page = Page.Render(() => [Button.OnClick(() => Toast.Info("Saved"))["Save"], Ui.Toast], Session());
        await page.Click("Save");

        await page.On("[data-rask-dismiss]").Click();

        Assert.DoesNotContain("data-ui-toast-dialog", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_toast_the_host_is_on_the_page_closed_empty_and_without_a_class_to_lay_it_out()
    {
        var page = Page.Render(() => [Ui.Toast.TopEnd, Ui.ToastGroup[Ui.Toast]], Session());

        var (single, group) = (page.Find("[data-ui-toast]"), page.Find("[data-ui-toast-group]"));

        // As Flux's <ui-toast> is: there before any toast, so one arriving is a change inside it.
        Assert.Equal("<div popover=\"manual\" data-ui-toast data-position=\"top end\" data-rask-popover-open=\"false\" role=\"status\"></div>", page.Html[..page.Html.IndexOf("<div popover=\"manual\" data-ui-toast-group", StringComparison.Ordinal)]);
        Assert.Equal("false", group.Attribute("data-rask-popover-open"));
        // `flex` on a closed popover would lay a transparent box over the whole page.
        Assert.Null(single.Attribute("class"));
        Assert.Null(group.Attribute("class"));
    }

    [Fact]
    public async Task Inside_a_group_the_layouts_toast_stacks_what_is_raised()
    {
        var page = Page.Render(
            () => [Button.OnClick(() => Toast.Info("Saved"))["Save"], Ui.ToastGroup.Expanded()[Ui.Toast]], Session());

        await page.Click("Save");
        await page.Click("Save");

        Assert.Contains("data-ui-toast-group", page.Html, StringComparison.Ordinal);
        Assert.Contains("data-expanded", page.Html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(page.Html, "data-ui-toast-dialog").Count);
    }

    [Fact]
    public async Task A_stack_keeps_its_toasts_when_the_page_that_places_it_renders_again()
    {
        var page = Page.Render(new ToastStackHost(), Session());

        await page.Click("Save");
        await page.Click("Save");

        Assert.Contains("raised 2", page.Html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(page.Html, "data-ui-toast-dialog").Count);
    }

    private static ServiceProvider Session() =>
        new ServiceCollection().AddSingleton<IToaster, Toaster>().BuildServiceProvider();

    private static string Shape(string html) => Regex.Match(html, " d=\"([^\"]+)\"").Groups[1].Value;
}

/// <summary>A page that keeps its own state and places a stack, as an app's layout does.</summary>
public sealed partial class ToastStackHost : Component
{
    private int _raised;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Button.OnClick(() =>
        {
            _raised++;
            Toast.Info("Saved").UntilDismissed();
        })["Save"],
        Span[$"raised {_raised}"],
        Placed()
    ];

    private static Component Placed() => Ui.ToastGroup[Ui.Toast];
}
