namespace Rask.Site.Features.UiKit;

/// <summary>
///     Every example on Flux UI's editor page, drawn with <c>Ui.Editor</c>, and one that shows what the value is.
/// </summary>
/// <remarks>
///     The value is HTML the reader typed, so the demo shows it as TEXT — encoded, in a <c>&lt;pre&gt;</c> —
///     and never writes it back into the page as markup. An app that does want to render it sanitizes it first.
/// </remarks>
public sealed partial class UiKitEditorDemo : Component
{
    private const string ReleaseNotes =
        "<h3>What's changed</h3><ul><li><p>Markdown notation support</p></li><li><p>Accessible toolbar</p></li>"
        + "<li><p>Shortcut keys</p></li></ul><p><strong>Full changelog:</strong> "
        + "<a target=\"_blank\" rel=\"noopener noreferrer nofollow\" href=\"https://github.com/pal-tamas/rask/releases\">every release</a></p>";

    // The docs' own height example: the area starts at 100px rather than 200px.
    private const string Short = "**:data-[slot=content]:min-h-[100px]!";

    private string _copied = "Nothing copied yet";

    /// <summary>Starts the bound editor on a document, so the toolbar has something to show.</summary>
    public UiKitEditorDemo() => Notes = ReleaseNotes;

    // A property, because that is what a binding writes to — and set in code, so a trimmed build keeps its setter.
    private string? Notes { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-editor").Class("max-w-xl space-y-8")[
            Example("Bound",
                Ui.Editor.Bind(() => Notes).Label("Release notes").Description("Explain what's new in this release."),
                Pre.Data("testid", "ui-editor-value").Class("overflow-x-auto rounded-lg bg-zinc-100 p-3 text-xs whitespace-pre-wrap dark:bg-white/10")[
                    string.IsNullOrEmpty(Notes) ? "(empty)" : Notes
                ]),
            Example("Configuring items",
                Ui.Editor.Toolbar("heading | bold italic underline | align ~ undo redo").Placeholder("Write something...").Class(Short)),
            Example("Customization",
                Ui.Editor.Placeholder("Write something...").Class(Short)[
                    Ui.EditorToolbar[
                        Ui.EditorHeading, Ui.EditorSeparator,
                        Ui.EditorBold, Ui.EditorItalic, Ui.EditorStrike, Ui.EditorSeparator,
                        Ui.EditorBullet, Ui.EditorOrdered, Ui.EditorBlockquote, Ui.EditorSeparator,
                        Ui.EditorLink, Ui.EditorSeparator,
                        Ui.EditorAlign,
                        Ui.EditorSpacer,
                        Ui.EditorButton.Icon(Ui.IconName.Clipboard).IconVariant(Ui.IconVariant.Outline).Tooltip("Copy to clipboard").OnClick(Copy)
                    ],
                    Ui.EditorContent
                ],
                P.Data("testid", "ui-editor-copied").Class("text-sm text-ui-muted")[_copied]),
            Example("Every item",
                Ui.Editor
                    .Toolbar("heading | bold italic strike underline | subscript superscript highlight code | bullet ordered blockquote | link | align ~ undo redo")
                    .Placeholder("Write something...")
                    .Class(Short)),
            Example("Disabled", Ui.Editor.Value("<p>This one is read-only.</p>").Disabled(true).Class(Short)),
            Example("Invalid", Ui.Editor.Placeholder("Write something...").Invalid(true).Class(Short))
        ];

    // A toolbar button of the app's own: it acts on the value the first editor is bound to.
    private void Copy() => _copied = $"Copied {(Notes ?? string.Empty).Length} characters of HTML";

    private static Component Example(string title, params Component?[] parts) =>
        Div.Key(title).Data("example", title).Class("space-y-3")[
            H3.Class("text-sm font-medium text-ui-muted")[title],
            parts
        ];
}
