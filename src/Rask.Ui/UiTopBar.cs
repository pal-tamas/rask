namespace Rask.Ui;

/// <summary>The breadcrumb bar: what you are looking at, and how to get to a sibling of it.</summary>
public sealed partial class UiTopBar : Component
{
    /// <summary>Pushed to the trailing edge — links, never state an operator has to act on.</summary>
    public Component? Trailing { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Header.Class("flex items-center gap-1 border-b border-base-300 px-2 py-2 sm:gap-2 sm:px-4 sm:py-2.5")[
            Children ?? [],
            Trailing is null ? null : Div.Class("ml-auto flex items-center gap-1 pl-2 sm:gap-3")[Trailing]
        ];
}
