namespace Rask;

/// <summary>
///     A toolbar button of your own, Flux's <c>flux:editor.button</c>: an icon, text, or both.
/// </summary>
/// <remarks>
///     <code>
///     Ui.EditorButton.Icon(Ui.IconName.EllipsisHorizontal).Tooltip("More").OnClick(ShowMore)
///     </code>
///     A button that should act on the document reads it from the editor's bound value.
/// </remarks>
public sealed partial class UiEditorButton : Component
{
    /// <summary>Name of the icon to display in the button.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>The variant of the icon to display: mini without content, micro beside it.</summary>
    public Ui.IconVariant? IconVariant { get; set; }

    /// <summary>Text to display in a tooltip when hovering over the button.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Prevents interaction with the button.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Raised when the button is pressed.</summary>
    public Callback OnClick { get; set; }

    /// <summary>Extra classes for the button.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var content = (Children ?? []).Where(child => child is not null).ToList();
        var variant = IconVariant ?? (content.Count == 0 ? Ui.IconVariant.Mini : Ui.IconVariant.Micro);
        var button = UiEditorMarkup.Button(null, Class).OnClick(OnClick);
        if (Disabled == true)
        {
            button = button.Attributes(("type", "button"), ("disabled", ""), ("data-disabled", ""));
        }

        var drawn = button[Icon is { } name ? Ui.Icon.Name(name).Variant(variant) : null, content];

        // Flux wraps it as it wraps any button with a tooltip: the wrapper says inline-flex, not the toolbar's contents.
        return Tooltip is { } tooltip ? Ui.Tooltip.Content(tooltip).Class("inline-flex")[drawn] : drawn;
    }
}
