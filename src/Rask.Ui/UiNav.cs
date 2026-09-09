namespace Rask.Ui;

/// <summary>
/// The section tab bar: the underlined row that says which part of the console you are in.
/// </summary>
/// <remarks>
/// Scrolls sideways rather than wrapping. A wrapped tab bar changes the page's header height as the number
/// of registered batteries changes, which moves the content under an operator's thumb between one
/// deployment and the next; a scrolling one is always exactly one row tall.
/// </remarks>
public sealed partial class UiNav : Nav
{

    /// <inheritdoc />
    protected override string? ResolveClass() =>
        "flex items-stretch gap-4 overflow-x-auto border-b border-base-300 px-3 sm:gap-6 sm:px-5 "
            + "[-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden";
}
