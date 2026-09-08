namespace Rask.Ui;

/// <summary>
/// A row of choices where picking one narrows a list, with a way back to all of them.
/// </summary>
/// <remarks>
/// <para>
/// Radios rather than buttons, and that is what makes it work with no script: daisyUI's <c>filter</c>
/// hides the unpicked options once one is chosen and shows the reset in their place, entirely in CSS.
/// The group also gives a keyboard the arrow-key behaviour a row of buttons would have to reimplement.
/// </para>
/// <para>
/// <see cref="Selected" /> and <see cref="OnSelect" /> keep C# in step, so the page can act on the
/// choice and restore it later.
/// </para>
/// </remarks>
public sealed partial class UiFilter : Component
{
    /// <summary>The radio group's name, so two filters on one page do not fight.</summary>
    public required string Group { get; set; }

    /// <summary>The options offered, in order.</summary>
    public required IReadOnlyList<string> Options { get; set; }

    /// <summary>The chosen option, or <c>null</c> for none.</summary>
    public string? Selected { get; set; }

    /// <summary>Runs with the option chosen, or <c>null</c> when the reset is pressed.</summary>
    public Action<string?>? OnSelect { get; set; }

    /// <summary>The accessible name on the reset control. Defaults to "All".</summary>
    public string? ResetLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var reset = Input
            .Value("")
            .Checked(Selected is null)
            .Type(InputType.Radio)
            .Name(Group)
            .Class("btn btn-square filter-reset")
            .Aria(new Dictionary<string, string?> { ["label"] = ResetLabel ?? "All" });

        if (OnSelect is { } onReset)
        {
            reset = reset.OnChange(_ => onReset(null));
        }

        // A div, not a <form>. daisyUI's own example wraps this in one so a reset BUTTON can clear it,
        // but the reset here is a radio carrying `filter-reset` — the group already holds the state,
        // and a nested <form> inside somebody else's form is invalid HTML.
        return Div.Class(UiClass.Compose("filter", Class))[
            reset,
            Options.Select(option =>
            {
                // daisyUI draws the option's TEXT from the input's own value, so opening the
                // chain here is not only tidier than the escape hatch — it is the label as well.
                var choice = Input
                    .Value(option)
                    .Checked(Selected == option)
                    .Key(option)
                    .Type(InputType.Radio)
                    .Name(Group)
                    .Class("btn")
                    .Aria(new Dictionary<string, string?> { ["label"] = option });

                return OnSelect is { } select ? choice.OnChange(_ => select(option)) : choice;
            })
        ];
    }
}
