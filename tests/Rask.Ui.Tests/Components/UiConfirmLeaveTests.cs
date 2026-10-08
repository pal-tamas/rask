using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.ConfirmLeave</c>: the dialog a form's <c>ConfirmLeave("…")</c> asks in, as markup the runtime opens.
/// </summary>
/// <remarks>
///     What it does once a guarded form is left — opening, staying, leaving — is <c>ConfirmLeaveDialogTests</c> in
///     <c>Rask.Server.E2E.Tests</c>. These hold what that needs from the render: a closed <c>Ui.Modal</c>, the
///     two marks the runtime looks for, and the two labels.
/// </remarks>
public partial class UiConfirmLeaveTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void It_is_a_modal_dialog_rendered_closed_for_the_browser_to_open()
    {
        var html = Ui.ConfirmLeave.ToHtml();

        var dialog = Tag(html, "<dialog");

        Assert.StartsWith("<div class=\"inline\" data-ui-modal>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ui-confirm-leave\"", dialog, StringComparison.Ordinal);
        Assert.Contains("data-rask-modal=\"any\"", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-modal-open", dialog, StringComparison.Ordinal);
        // The attribute, not the `open:` variants in its class list.
        Assert.DoesNotMatch(@"\sopen(\s|=|>)", dialog);
        Assert.Contains("aria-label=\"Close modal\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_is_an_empty_heading_the_morph_leaves_to_the_browser()
    {
        var html = Ui.ConfirmLeave.ToHtml();

        var heading = Tag(html, "<h2");

        Assert.Contains("data-rask-leave=\"message\"", heading, StringComparison.Ordinal);
        Assert.Contains("data-rask-opaque", heading, StringComparison.Ordinal);
        Assert.Contains(heading + "</h2>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_labels_the_buttons_read_Stay_and_Leave()
    {
        var html = Ui.ConfirmLeave.ToHtml();

        var buttons = Buttons(html);

        Assert.Equal(["Stay", "Leave"], buttons.Select(button => button.Label));
    }

    [Fact]
    public void The_labels_given_are_the_labels_shown()
    {
        var html = Ui.ConfirmLeave.Stay("Nem").Leave("Igen").ToHtml();

        var buttons = Buttons(html);

        Assert.Equal(["Nem", "Igen"], buttons.Select(button => button.Label));
    }

    [Fact]
    public void Both_buttons_close_the_dialog_and_only_the_second_is_the_one_that_leaves()
    {
        var html = Ui.ConfirmLeave.ToHtml();

        var buttons = Buttons(html);

        Assert.All(buttons, button => Assert.Contains("command=\"close\"", button.Tag, StringComparison.Ordinal));
        Assert.All(buttons, button => Assert.Contains("commandfor=\"ui-confirm-leave\"", button.Tag, StringComparison.Ordinal));
        Assert.DoesNotContain("data-rask-leave", buttons[0].Tag, StringComparison.Ordinal);
        Assert.Contains("data-rask-leave=\"go\"", buttons[1].Tag, StringComparison.Ordinal);
    }

    // The two buttons of the row, in order: not the close button in the corner, which has no text.
    private static List<(string Tag, string Label)> Buttons(string html)
    {
        var row = html[html.IndexOf("data-ui-spacer", StringComparison.Ordinal)..];
        List<(string, string)> buttons = [];
        for (var at = row.IndexOf("<button", StringComparison.Ordinal); at >= 0; at = row.IndexOf("<button", at + 1, StringComparison.Ordinal))
        {
            var end = row.IndexOf("</button>", at, StringComparison.Ordinal);
            var tag = row[at..(row.IndexOf('>', at) + 1)];
            var label = System.Text.RegularExpressions.Regex.Replace(row[(at + tag.Length)..end], "<[^>]+>", string.Empty).Trim();
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
