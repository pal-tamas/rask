namespace Rask.UiTests.Components;

/// <summary>
///     A toast, and a stack of them.
/// </summary>
/// <remarks>
///     <para>
///     The load-bearing decision is that the PAGE owns the list. A toast that dismissed itself by hiding its
///     own element would leave the page believing a toast is up that nobody can see — and the next render
///     would put it back. So <c>Duration</c> asks the runtime to CLICK the toast's own dismiss control, which
///     runs the page's handler, which takes the toast out of the page's state.
///     </para>
///     <para>
///     What is pinned here is the markup that contract rests on: the dismiss control the runtime looks for,
///     and the attribute that tells it how long to wait.
///     </para>
/// </remarks>
public partial class UiToastTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void An_outcome_is_announced_politely()
    {
        // The reader did this; it does not need interrupting to hear that it worked.
        Assert.Contains("role=\"status\"",
            Ui.Toast.Message("Saved").ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_failure_interrupts()
    {
        // The exception, and the reason for it: the reader is usually about to act on the thing that failed.
        Assert.Contains("role=\"alert\"",
            Ui.Toast.Message("Could not save").Tone(Ui.Tone.Error).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_handler_there_is_nothing_to_dismiss_with() =>
        Assert.DoesNotContain("data-rask-dismiss",
            Ui.Toast.Message("Saved").ToHtml(), StringComparison.Ordinal);

    [Fact]
    public void The_dismiss_button_carries_the_runtimes_own_hook()
    {
        // data-rask-dismiss is the convention the focus trap already presses on Escape, and what Duration
        // clicks. Naming it here is what lets the runtime find it without knowing anything about toasts.
        var html = Ui.Toast.Message("Saved").OnDismiss(() => { }).ToHtml();

        Assert.Contains("data-rask-dismiss", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Dismiss\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duration_asks_the_runtime_to_wait_and_then_press_it()
    {
        Assert.Contains("data-rask-dismiss-after=\"4000\"",
            Ui.Toast.Message("Saved").OnDismiss(() => { }).Duration(TimeSpan.FromSeconds(4)).ToHtml(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_duration_with_nothing_to_press_is_not_written()
    {
        // The runtime dismisses by CLICKING the toast's own control. With no handler there is no control, so
        // arming a timer would start a countdown that could never finish — better to say nothing.
        Assert.DoesNotContain("data-rask-dismiss-after",
            Ui.Toast.Message("Saved").Duration(TimeSpan.FromSeconds(4)).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_heading_puts_the_message_on_a_second_line()
    {
        var html = Ui.Toast.Message("Three files were skipped.").Heading("Upload finished").ToHtml();

        Assert.Contains("Upload finished", html, StringComparison.Ordinal);
        Assert.Contains("Three files were skipped.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_action_sits_in_the_row() =>
        Assert.Contains("Undo",
            Ui.Toast.Message("Deleted").Action(Ui.Button.Size(Ui.Size.Xs)["Undo"]).ToHtml(),
            StringComparison.Ordinal);

    [Fact]
    public void On_its_own_a_toast_pins_itself_to_a_corner()
    {
        Assert.Contains("bottom-3", Ui.Toast.Message("Saved").ToHtml(), StringComparison.Ordinal);
        Assert.Contains("top-3",
            Ui.Toast.Message("Saved").Position(Ui.Position.Top).ToHtml(), StringComparison.Ordinal);
        Assert.Contains("sm:right-3",
            Ui.Toast.Message("Saved").Align(Ui.Align.End).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Inside_a_toaster_the_stack_is_placed_and_the_toast_is_not()
    {
        // Two toasts that each pinned themselves to the same corner would sit on top of one another.
        var html = Ui.Toaster[
            Ui.Toast.Key("a").Message("First"),
            Ui.Toast.Key("b").Message("Second")
        ].ToHtml();

        Assert.Equal(1, Occurrences(html, "bottom-3"));
        Assert.Equal(2, Occurrences(html, "role=\"status\""));
    }

    [Fact]
    public void An_empty_toaster_renders_nothing()
    {
        // Otherwise every page carries a fixed element over its own content for the toasts it does not have.
        Assert.Equal("", Ui.Toaster.ToHtml());
    }

    [Fact]
    public void The_stack_does_not_swallow_clicks_on_the_page_under_it()
    {
        // A fixed container spanning the corner would eat the clicks in the gaps between its toasts.
        var html = Ui.Toaster[Ui.Toast.Key("a").Message("First")].ToHtml();

        Assert.Contains("pointer-events-none", html, StringComparison.Ordinal);
        Assert.Contains("pointer-events-auto", html, StringComparison.Ordinal);
    }

    private static int Occurrences(string haystack, string needle)
    {
        var n = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
