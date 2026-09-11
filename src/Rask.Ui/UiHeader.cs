namespace Rask.Ui;

/// <summary>A page heading with its caption and an optional row of controls.</summary>
/// <remarks>
/// No margin of its own: <see cref="UiMain" /> spaces the sections it holds, and a heading that carried a margin
/// as well would sit twice as far from the first section as every section sits from the next.
/// </remarks>
public sealed partial class UiHeader : Component
{
    public required string Heading { get; set; }

    public string? Caption { get; set; }

    public Component? Actions { get; set; }

    /// <summary>Shown before the heading, so a queue page is recognisable at a glance.</summary>
    public UiIconName? Icon { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-x-3 gap-y-2")[
            Icon is { } icon ? UiIcon.Name(icon).Class("size-5 shrink-0 opacity-60") : null,
            H1.Class(UiStyles.Heading)[Heading],
            Caption is null ? null : Span.Class("text-xs opacity-60")[Caption],
            // Full width on its own line below sm, so a row of actions never squeezes the heading to
            // nothing; trailing-aligned beside it from sm up.
            Actions is null ? null : Div.Class("flex w-full flex-wrap gap-2 sm:ml-auto sm:w-auto")[Actions]
        ];
}
