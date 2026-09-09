namespace Rask.Ui;

/// <summary>A page heading with its caption and an optional row of controls.</summary>
public sealed partial class UiHeader : Component
{
    public required string Heading { get; set; }

    public new string? Caption { get; set; }

    public Component? Actions { get; set; }

    /// <summary>Shown before the heading, so a queue page is recognisable at a glance.</summary>
    public UiIconName? Icon { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 sm:mb-5")[
            Icon is { } icon ? UiIcon.Name(icon).Class("size-5 shrink-0 opacity-60") : null,
            H1.Class(UiStyles.Heading)[Heading],
            Caption is null ? null : Span.Class("text-xs opacity-60")[Caption],
            // Full width on its own line below sm, so a row of actions never squeezes the heading to
            // nothing; trailing-aligned beside it from sm up.
            Actions is null ? null : Div.Class("flex w-full flex-wrap gap-2 sm:ml-auto sm:w-auto")[Actions]
        ];
}
