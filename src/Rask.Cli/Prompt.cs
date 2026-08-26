using Spectre.Console;

namespace Rask.Cli;

/// <summary>
/// The CLI's interactive questions, rendered by Spectre.Console over <see cref="IConsole"/>.
/// <see cref="Select"/> and <see cref="MultiSelect"/> are arrow-key lists; <see cref="Ask"/> and
/// <see cref="Confirm"/> read a line.
/// <para>
/// Every method self-guards on <see cref="Interactive"/> and returns the default when there is no
/// terminal, so a piped or scripted run can never block on a question nobody can answer. Input
/// exhaustion is treated the same way — a prompt that runs out of answers yields its default rather
/// than throwing, which is what keeps a half-scripted test from hanging the suite.
/// </para>
/// <para>
/// Labels and option text are <b>markup</b>, so a caller can emphasize part of a question. Every call
/// site passes a literal; anything derived from user input must go through <see cref="Markup.Escape"/>
/// first, which is why the validation path below escapes on the caller's behalf.
/// </para>
/// </summary>
internal sealed class Prompt(IConsole console)
{
    /// <summary>True when stdin is a terminal — the only time a question is worth asking.</summary>
    public bool Interactive => !console.IsInputRedirected;

    /// <summary>
    /// Ask for a line of text. With a <paramref name="default"/>, an empty answer accepts it; without one
    /// the question repeats until non-empty. <paramref name="validate"/> returns an error message to
    /// re-ask with, or null to accept — so a bad answer is caught here rather than after the command has
    /// started doing work.
    /// </summary>
    public string Ask(string label, string? @default = null, Func<string, string?>? validate = null)
    {
        if (!Interactive)
        {
            return @default ?? string.Empty;
        }

        var prompt = new TextPrompt<string>(label + ":");
        if (@default is not null)
        {
            prompt.DefaultValue(@default);
        }

        if (validate is not null)
        {
            // The message quotes what the user typed, so it has to be escaped before it reaches the
            // markup parser — a project name of "My[App]" would otherwise blow up the error path itself.
            prompt.Validate(value => validate(value) is { } error
                ? ValidationResult.Error(Markup.Escape(error))
                : ValidationResult.Success());
        }

        return Show(prompt, @default ?? string.Empty);
    }

    /// <summary>
    /// Ask a yes/no question. <c>y</c>/<c>yes</c> and <c>n</c>/<c>no</c> are accepted, case-insensitively;
    /// an empty answer accepts <paramref name="default"/>.
    /// </summary>
    /// <remarks>
    /// Built on a text prompt rather than Spectre's <c>ConfirmationPrompt</c>, which matches a single
    /// character and so rejects a typed-out "yes" — the answer people give most readily to a question
    /// that is about to change their project.
    /// </remarks>
    public bool Confirm(string label, bool @default)
    {
        if (!Interactive)
        {
            return @default;
        }

        var answer = Show(
            new TextPrompt<string>($"{label} [dim][[{(@default ? "Y/n" : "y/N")}]][/]")
                .AllowEmpty()
                // Empty is a valid answer — it means "the default" — so only a typed non-answer re-asks.
                .Validate(value => value.Trim().Length == 0 || IsYes(value) is not null
                    ? ValidationResult.Success()
                    : ValidationResult.Error("[red]Answer y or n.[/]")),
            string.Empty);

        return IsYes(answer) ?? @default;
    }

    /// <summary>True for yes, false for no, null for "not an answer". Empty means "take the default".</summary>
    private static bool? IsYes(string answer)
    {
        answer = answer.Trim();
        return answer.Length == 0 ? null
            : answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase) ? true
            : answer.Equals("n", StringComparison.OrdinalIgnoreCase) || answer.Equals("no", StringComparison.OrdinalIgnoreCase) ? false
            : null;
    }

    /// <summary>
    /// Choose one of <paramref name="options"/> (value + label) with the arrow keys. Falls back to
    /// <paramref name="default"/> when there is no terminal.
    /// <para>
    /// The default is listed first, so it is the row already highlighted when the list appears and enter
    /// accepts it. A list prompt has no way to start on a row other than the top one, so the ordering is
    /// what makes "just press enter" mean the same thing as omitting the flag.
    /// </para>
    /// </summary>
    public string Select(string label, IReadOnlyList<(string Value, string Label)> options, string @default)
    {
        if (!Interactive || options.Count == 0)
        {
            return @default;
        }

        var ordered = options
            .OrderByDescending(o => o.Value.Equals(@default, StringComparison.Ordinal))
            .Select(o => o.Value)
            .ToArray();

        var prompt = new SelectionPrompt<string>()
            .Title(label)
            .PageSize(Math.Max(3, Math.Min(options.Count + 1, 15)))
            .AddChoices(ordered)
            .UseConverter(value => LabelOf(options, value, @default));

        return Show(prompt, @default);
    }

    /// <summary>
    /// Choose any number of <paramref name="options"/> with space, confirm with enter. Returns the selected
    /// values in the order they were offered. Selecting nothing is allowed and yields an empty list.
    /// </summary>
    /// <remarks>
    /// This is the shape a long list of independent choices wants. Asking the same thing as a run of
    /// yes/no questions makes the user answer every one to reach the one they came for, and gives them no
    /// view of the whole menu at once.
    ///
    /// <para>
    /// <paramref name="selected"/> pre-ticks entries, which turns the question from "what would you like?"
    /// into "anything you don't want?". Both the fall-back on a closed input and the empty-options case
    /// return it unchanged, so the answer to a question that could not be asked is what was already true
    /// rather than nothing.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> MultiSelect(
        string label,
        IReadOnlyList<(string Value, string Label)> options,
        IReadOnlyCollection<string>? selected = null)
    {
        var preselected = selected ?? [];
        List<string> fallback = [.. options.Select(o => o.Value).Where(preselected.Contains)];

        if (!Interactive || options.Count == 0)
        {
            return fallback;
        }

        var prompt = new MultiSelectionPrompt<string>()
            .Title(label)
            .NotRequired()
            .PageSize(Math.Max(3, Math.Min(options.Count + 1, 15)))
            .InstructionsText("[dim](space to toggle, enter to accept, nothing selected is fine)[/]")
            .UseConverter(value => LabelOf(options, value, @default: null));

        foreach (var option in options)
        {
            var item = prompt.AddChoice(option.Value);
            if (preselected.Contains(option.Value))
            {
                item.Select();
            }
        }

        // Spectre returns the values in selection order; re-project through the offered order so the
        // resulting command line reads the same however the user clicked through the list.
        var chosen = Show(prompt, fallback);
        return [.. options.Select(o => o.Value).Where(chosen.Contains)];
    }

    private static string LabelOf(IReadOnlyList<(string Value, string Label)> options, string value, string? @default)
    {
        var label = options.First(o => o.Value.Equals(value, StringComparison.Ordinal)).Label;
        return value.Equals(@default, StringComparison.Ordinal) ? label + " (default)" : label;
    }

    /// <summary>
    /// Run a prompt, yielding <paramref name="default"/> if the input ends mid-question. Spectre throws
    /// when it cannot read another key; a CLI that is mid-wizard should finish with the defaults rather
    /// than surface a stack trace.
    /// </summary>
    private T Show<T>(IPrompt<T> prompt, T @default)
    {
        try
        {
            return prompt.Show(console.Ansi);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or NotSupportedException)
        {
            return @default;
        }
    }
}
