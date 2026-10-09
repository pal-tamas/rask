namespace Rask;

/// <summary>
///     An editor's toolbar, Flux's <c>flux:editor.toolbar</c>: the items named in <see cref="Items" />, or the
///     ones composed inside it.
/// </summary>
/// <remarks>
///     <code>
///     Ui.EditorToolbar.Items("heading | bold italic underline | align ~ undo redo")
///     Ui.EditorToolbar[Ui.EditorBold, Ui.EditorItalic, Ui.EditorSpacer, Ui.EditorButton.Tooltip("More")]
///     </code>
/// </remarks>
public sealed partial class UiEditorToolbar : Component
{
    /// <summary>What an editor shows when nothing says otherwise.</summary>
    internal const string Default = "heading | bold italic strike | bullet ordered blockquote | link | align";

    /// <summary>
    ///     Space-separated list of toolbar items to display; <c>|</c> is a separator and <c>~</c> a spacer.
    ///     Left unset, and with nothing composed inside, the default toolbar is shown.
    /// </summary>
    public string? Items { get; set; }

    /// <summary>Extra classes for the toolbar.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var composed = (Children ?? []).Where(child => child is not null).ToList();
        var bar = Div
            .Class(UiClass.Compose(
                "block overflow-x-auto rounded-t-[calc(var(--radius-lg)-1px)] border-b border-zinc-200 bg-zinc-50 "
                + "dark:border-white/10 dark:bg-white/[6%]",
                Class))
            .Role("toolbar")
            .Aria("label", RaskStrings.Get(RaskString.EditorToolbar, "Formatting"));
        if (Context.Get<UiEditorScope>() is { } editor)
        {
            bar = bar.Aria(new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = RaskStrings.Get(RaskString.EditorToolbar, "Formatting"), ["controls"] = editor.InputId });
        }

        return bar[
            Context.Provide(new UiEditorToolbarScope())[
                Div.Class("flex items-center gap-2 p-2")[
                    composed.Count != 0 ? composed : Named(Items ?? Default)
                ]
            ]
        ];
    }

    private static IEnumerable<Component?> Named(string items) =>
        items.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Item);

    // Flux resolves a name to a Blade file; here a name is one of the kit's own items, and anything else is
    // composed as a child of the toolbar instead.
    private static Component Item(string name) => name switch
    {
        "|" or "separator" => Ui.EditorSeparator,
        "~" or "spacer" => Ui.EditorSpacer,
        "heading" => Ui.EditorHeading,
        "bold" => Ui.EditorBold,
        "italic" => Ui.EditorItalic,
        "strike" => Ui.EditorStrike,
        "underline" => Ui.EditorUnderline,
        "bullet" => Ui.EditorBullet,
        "ordered" => Ui.EditorOrdered,
        "blockquote" => Ui.EditorBlockquote,
        "subscript" => Ui.EditorSubscript,
        "superscript" => Ui.EditorSuperscript,
        "highlight" => Ui.EditorHighlight,
        "link" => Ui.EditorLink,
        "code" => Ui.EditorCode,
        "align" => Ui.EditorAlign,
        "undo" => Ui.EditorUndo,
        "redo" => Ui.EditorRedo,
        _ => throw new ArgumentException(
            $"'{name}' is not a toolbar item. The items are heading, bold, italic, strike, underline, bullet, ordered, "
            + "blockquote, subscript, superscript, highlight, link, code, align, undo and redo, with | for a separator "
            + "and ~ for a spacer. An item of your own is composed inside Ui.EditorToolbar[…] as a Ui.EditorButton.",
            nameof(name)),
    };
}
