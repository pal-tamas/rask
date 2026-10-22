namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class EditorCustomItemExample : Component
{
    private bool _copied;

    // Set in code, so a trimmed build keeps the setter the binding writes through.
    public EditorCustomItemExample() => Content = "";

    private string? Content { get; set; }

    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Editor.Bind(() => Content).Live().Placeholder("Write something...")[
                Ui.EditorToolbar[
                    Ui.EditorHeading, Ui.EditorSeparator,
                    Ui.EditorBold, Ui.EditorItalic, Ui.EditorStrike, Ui.EditorSeparator,
                    Ui.EditorBullet, Ui.EditorOrdered, Ui.EditorBlockquote, Ui.EditorSeparator,
                    Ui.EditorLink, Ui.EditorSeparator,
                    Ui.EditorAlign,
                    Ui.EditorSpacer,
                    Ui.EditorButton.Icon(_copied ? Ui.IconName.ClipboardDocumentCheck : Ui.IconName.Clipboard)
                        .IconVariant(Ui.IconVariant.Outline).Tooltip("Copy to clipboard").OnClick(Copy)
                ],
                Ui.EditorContent
            ],
            Ui.Text.Data("testid", "ui-editor-copied")[_copied ? $"Copied {(Content ?? "").Length} characters of HTML." : "Nothing copied yet."]
        ];

    // A toolbar item of the app's own is a button that runs C#: this one hands the editor's HTML to the clipboard.
    private async Task Copy()
    {
        await Navigator.Clipboard.WriteText(Content ?? "");
        _copied = true;
    }
}
