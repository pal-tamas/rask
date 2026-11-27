using System.Text.RegularExpressions;
using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.Confirm</c>: the dialog an action's <c>.Confirm("…")</c> asks in, as markup the runtime opens.
/// </summary>
/// <remarks>
///     What it does once an action asks — opening, No, Yes, the typed answer — is <c>ConfirmDialogTests</c> in
///     <c>Rask.Server.E2E.Tests</c>. These hold what that needs from the render: a closed <c>Ui.Modal</c> drawn as
///     <c>Ui.ConfirmLeave</c> is, the parts the runtime looks for, and the labels.
/// </remarks>
public partial class UiConfirmTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_is_a_modal_dialog_rendered_closed_for_the_browser_to_open()
    {
        var html = Ui.Confirm.ToHtml();

        var dialog = Tag(html, "<dialog");

        Assert.StartsWith("<div class=\"inline\" data-ui-modal>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ui-confirm\"", dialog, StringComparison.Ordinal);
        Assert.Contains("data-rask-modal=\"any\"", dialog, StringComparison.Ordinal);
        Assert.Contains("min-w-[22rem]", dialog, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\sopen(\s|=|>)", dialog);
    }

    [Fact]
    public void It_is_drawn_as_the_leave_dialog_is_but_for_its_own_parts_and_words()
    {
        var confirm = Ui.Confirm.Heading("Sure?").No("Stay").Yes("Leave").ToHtml();
        var leave = Ui.ConfirmLeave.Heading("Sure?").ToHtml();

        var withoutTheField = Regex.Replace(confirm, "<div hidden data-rask-confirm-dialog=\"field\">.*?</div></div>", string.Empty);
        // The title a question may bring of its own is Ui.Confirm's alone.
        var withoutItsTitle = Regex.Replace(
            withoutTheField, " data-rask-confirm-dialog=\"heading\" data-rask-confirm-default=\"[^\"]*\" data-rask-opaque", string.Empty);
        // So are the labels and the look a question from code words for itself.
        var withoutItsLabels = Regex.Replace(
            withoutItsTitle, "( data-rask-confirm-dialog=\"no\")? data-rask-confirm-default=\"[^\"]*\" data-rask-opaque", string.Empty);
        var withoutItsTones = Regex.Replace(
            withoutItsLabels, " data-rask-confirm-tone=\"[^\"]*\" data-rask-confirm-other=\"[^\"]*\"", string.Empty);
        var asLeave = withoutItsTones
            .Replace(" id=\"ui-confirm-message\"", string.Empty, StringComparison.Ordinal)
            .Replace("ui-confirm", "ui-confirm-leave", StringComparison.Ordinal)
            .Replace("data-rask-confirm-dialog", "data-rask-leave", StringComparison.Ordinal);

        Assert.Equal(Normalized(leave), Normalized(asLeave));
    }

    [Fact]
    public void The_heading_titles_the_dialog_and_the_question_is_an_empty_sentence_the_morph_leaves_to_the_browser()
    {
        var html = Ui.Confirm.Heading("Megerősítés").ToHtml().AsText();

        var title = Tag(html, "<div class=\"font-medium");

        Assert.Contains(title + "Megerősítés</div>", html, StringComparison.Ordinal);
        Assert.Contains("data-ui-heading", title, StringComparison.Ordinal);
        Assert.Matches("<p[^>]*id=\"ui-confirm-message\"[^>]*data-rask-confirm-dialog=\"message\"[^>]*data-rask-opaque[^>]*></p>", html);
    }

    [Fact]
    public void The_title_is_the_runtimes_to_write_and_keeps_the_layouts_for_a_question_with_none_of_its_own()
    {
        var html = Ui.Confirm.Heading("Megerősítés").ToHtml().AsText();

        var title = Tag(html, "<div class=\"font-medium");

        Assert.Contains("data-rask-confirm-dialog=\"heading\"", title, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-default=\"Megerősítés\"", title, StringComparison.Ordinal);
        Assert.Contains("data-rask-opaque", title, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_words_given_it_reads_Confirm_No_and_Yes()
    {
        var html = Ui.Confirm.ToHtml();

        var buttons = Buttons(html);

        Assert.Contains(">Confirm</div>", html, StringComparison.Ordinal);
        Assert.Equal(["No", "Yes"], buttons.Select(button => button.Label));
    }

    [Fact]
    public void The_words_given_are_the_words_shown()
    {
        var html = Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen").ToHtml().AsText();

        var buttons = Buttons(html);

        Assert.Contains(">Megerősítés</div>", html, StringComparison.Ordinal);
        Assert.Equal(["Nem", "Igen"], buttons.Select(button => button.Label));
    }

    [Fact]
    public void No_is_a_ghost_button_Yes_a_danger_one_and_only_Yes_is_the_one_that_lets_the_action_go()
    {
        var html = Ui.Confirm.ToHtml();

        var buttons = Buttons(html);

        Assert.All(buttons, button => Assert.Contains("command=\"close\"", button.Tag, StringComparison.Ordinal));
        Assert.All(buttons, button => Assert.Contains("commandfor=\"ui-confirm\"", button.Tag, StringComparison.Ordinal));
        Assert.Equal(Ui.Button.Ghost.ToHtml()[..20], buttons[0].Tag[..20]);
        Assert.Contains("data-rask-confirm-dialog=\"no\"", buttons[0].Tag, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-dialog=\"go\"", buttons[1].Tag, StringComparison.Ordinal);
        Assert.Contains("bg-red-500", buttons[1].Tag, StringComparison.Ordinal);
    }

    [Fact]
    public void Yes_drawn_in_danger_carries_the_primary_look_for_a_question_that_asks_in_it()
    {
        var html = Ui.Confirm.ToHtml();

        var yes = Buttons(html)[1].Tag;

        Assert.Contains("data-rask-confirm-tone=\"danger\"", yes, StringComparison.Ordinal);
        Assert.Matches("data-rask-confirm-other=\"[^\"]*bg-accent[^\"]*\"", yes);
        Assert.DoesNotMatch("data-rask-confirm-other=\"[^\"]*bg-red-500", yes);
    }

    [Fact]
    public void Primary_draws_Yes_as_the_primary_button_carrying_the_danger_look()
    {
        var html = Ui.Confirm.Primary().ToHtml();

        var yes = Buttons(html)[1].Tag;
        var drawn = Regex.Match(yes, "class=\"([^\"]*)\"").Groups[1].Value;

        Assert.Contains("bg-accent", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("bg-red-500", drawn, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-dialog=\"go\"", yes, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-tone=\"primary\"", yes, StringComparison.Ordinal);
        Assert.Matches("data-rask-confirm-other=\"[^\"]*bg-red-500[^\"]*\"", yes);
    }

    [Fact]
    public void The_labels_are_the_runtimes_to_write_and_keep_the_layouts_words()
    {
        var html = Ui.Confirm.No("Nem").Yes("Igen").ToHtml().AsText();

        var buttons = Buttons(html);
        var (no, yes) = (buttons[0].Tag, buttons[1].Tag);

        // As Flux's: Cancel holds its words alone, the delete the spinner and its words in a span after it.
        Assert.Contains(no + "Nem</button>", html, StringComparison.Ordinal);
        var inYes = html[(html.IndexOf(yes, StringComparison.Ordinal) + yes.Length)..];
        inYes = inYes[..inYes.IndexOf("</button>", StringComparison.Ordinal)];
        Assert.StartsWith("<div class=\"absolute inset-0", inYes, StringComparison.Ordinal);
        Assert.Contains("data-ui-loading-indicator><svg", inYes, StringComparison.Ordinal);
        Assert.Matches("</svg></div><span class=\"[^\"]*\">Igen</span>$", inYes);
        Assert.Contains("data-rask-confirm-dialog=\"no\"", no, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-dialog=\"go\"", yes, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-default=\"Nem\" data-rask-opaque", no, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-default=\"Igen\" data-rask-opaque", yes, StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_an_answer_is_typed_into_is_hidden_unbound_and_named_by_the_question()
    {
        var html = Ui.Confirm.ToHtml();

        var field = html[html.IndexOf("data-rask-confirm-dialog=\"field\"", StringComparison.Ordinal)..];
        var input = Tag(field, "<input");

        Assert.Contains("<div hidden data-rask-confirm-dialog=\"field\">", html, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"ui-confirm-message\"", input, StringComparison.Ordinal);
        Assert.Contains("data-rask-unguarded", input, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-", input, StringComparison.Ordinal);
        Assert.DoesNotContain(" name=", input, StringComparison.Ordinal);
        Assert.DoesNotContain(" value=", input, StringComparison.Ordinal);
    }

    private static string Normalized(string html) => Regex.Replace(html, "ui-[a-z0-9-]*-[0-9a-f]{6,}", "ui-id");

    // The two buttons of the row, in order: not the close button in the corner, which has no text.
    private static List<(string Tag, string Label)> Buttons(string html)
    {
        var row = html[html.IndexOf("data-ui-spacer", StringComparison.Ordinal)..];
        List<(string, string)> buttons = [];
        for (var at = row.IndexOf("<button", StringComparison.Ordinal); at >= 0; at = row.IndexOf("<button", at + 1, StringComparison.Ordinal))
        {
            var end = row.IndexOf("</button>", at, StringComparison.Ordinal);
            var tag = Regex.Match(row[at..], "^<(?:[^>\"]|\"[^\"]*\")*>").Value;
            var label = Regex.Replace(row[(at + tag.Length)..end], "<(?:[^>\"]|\"[^\"]*\")*>", string.Empty).Trim();
            if (label.Length > 0)
            {
                buttons.Add((tag, label));
            }
        }

        return buttons;
    }

    private static string Tag(string html, string prefix)
    {
        var start = html.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no tag starting {prefix}");
        return html[start..(html.IndexOf('>', start) + 1)];
    }
}
