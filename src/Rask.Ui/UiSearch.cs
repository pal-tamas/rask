namespace Rask.Ui;

/// <summary>
/// A search field: a leading icon, and the filter it drives.
/// </summary>
/// <remarks>
/// The accessible name is required and separate from the placeholder, which is not one — a placeholder
/// disappears exactly when typing starts, taking the field's only label with it.
/// </remarks>
public sealed partial class UiSearch : Component
{
    public required string Placeholder { get; set; }

    /// <summary>
    ///     The accessible name. Named for what it is rather than called <c>Label</c>, because inside a
    ///     markup host a property of that name would shadow the chain's entry for the <c>&lt;label&gt;</c>
    ///     element this component renders.
    /// </summary>
    public required string AccessibleLabel { get; set; }

    public string? Value { get; set; }

    public UiSize? Size { get; set; }

    public Callback<string>? OnSearch { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var input = Input
            .Value(Value ?? string.Empty)
            .Type(InputType.Search)
            .Placeholder(Placeholder)
            .Aria(new Dictionary<string, string?> { ["label"] = AccessibleLabel })
            .Class("grow");

        if (OnSearch is { } search)
        {
            input = input.OnChange(search);
        }

        // daisyUI's `input` is a WRAPPER that lays out whatever sits inside it, so the icon goes in the
        // label beside the field rather than being absolutely positioned over it. Full width on a phone,
        // a sane column from sm up: on a 360px screen a fixed-width search box either overflows the row or
        // leaves the rest of it stranded.
        return Label
            .Class(UiClass.Compose(
                "input w-full sm:w-72",
                Size is { } size ? UiClassNames.InputSize(size) : "",
                Class))[
            UiIcon.Name(UiIconName.Search).Class("size-4 shrink-0 opacity-60"),
            input
        ];
    }
}
