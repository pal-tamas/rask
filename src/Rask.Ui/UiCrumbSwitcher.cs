namespace Rask.Ui;

/// <summary>
/// A breadcrumb level you can actually switch: a native select wearing the crumb's clothes.
/// </summary>
/// <remarks>
/// <para>
/// The reference opens a custom menu here. A menu is a popover, a popover is a key listener and an outside
/// click, and the console ships no JavaScript — so this is a real <c>&lt;select&gt;</c> with the chrome
/// stripped off it. That is not a consolation prize: it is keyboard-navigable for free, it announces itself
/// correctly, and on a phone it opens the platform's own picker, which is a better control than a menu
/// re-implemented in a div would have been.
/// </para>
/// <para>
/// The same trick the log's category filter already uses. Navigating on change rather than on a submit
/// means there is no button to press and nothing to forget to press.
/// </para>
/// </remarks>
public sealed partial class UiCrumbSwitcher : Component
{
    /// <summary>The accessible name. There is no visible label — the crumb's position is the label.</summary>
    public new required string Label { get; set; }

    /// <summary>The option currently selected.</summary>
    public required string Value { get; set; }

    public required IReadOnlyList<(string Value, string Text)> Choices { get; set; }

    public Func<string, Task>? OnSelect { get; set; }

    public UiIconName? Icon { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var options = new List<Component?>();
        foreach (var (value, text) in Choices)
        {
            options.Add(Option
                .Key(value)
                .Value(value)
                .Selected(string.Equals(value, Value, StringComparison.Ordinal))[text]);
        }

        var select = Select
            .Value(Value)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            // appearance-none strips the platform arrow so the crumb's own chevron can sit where the
            // reference puts it; the select underneath is otherwise completely ordinary.
            .Class(
                "min-h-11 w-full cursor-pointer appearance-none truncate rounded-lg border border-transparent "
                + "bg-transparent py-1.5 pr-7 text-sm text-base-content hover:bg-base-200 focus-visible:outline-2 "
                + "focus-visible:outline-offset-2 focus-visible:outline-primary sm:min-h-0 "
                + (Icon is null ? "pl-2" : "pl-8"));

        if (OnSelect is { } select_)
        {
            select = select.OnChangeAsync(select_);
        }

        return Div.Class("relative flex min-w-0 max-w-[9rem] items-center sm:max-w-[16rem]")[
            Icon is { } icon
                ? UiIcon.Name(icon).Class("pointer-events-none absolute left-2 size-4 shrink-0 opacity-60")
                : null,
            select[options],
            UiIcon
                .Name(UiIconName.ChevronUpDown)
                .Class("pointer-events-none absolute right-2 size-4 shrink-0 opacity-60")
        ];
    }
}
