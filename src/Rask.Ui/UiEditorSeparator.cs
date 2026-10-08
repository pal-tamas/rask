namespace Rask;

/// <summary>
///     A hairline between two groups of toolbar items: <c>|</c> in a toolbar's item list, Flux's
///     <c>flux:editor.separator</c>.
/// </summary>
public sealed partial class UiEditorSeparator : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("h-4 w-px bg-zinc-200 dark:bg-white/20").Data("orientation", "vertical").Role("none");
}
