namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class EditorExample : Component
{
    public EditorExample() =>
        Notes = "<h3>What's changed</h3><ul><li><p>Markdown notation support</p></li><li><p>Accessible toolbar</p></li>"
                + "<li><p>Shortcut keys</p></li></ul>";

    private string? Notes { get; set; }

    // The value is HTML the reader typed: it is shown here as text, and never written into the page as markup.
    protected override Component? Render() =>
        Div.Class("space-y-4")[
            Ui.Editor.Bind(() => Notes).Live().Label("Release notes").Description("Explain what's new in this release."),
            Ui.Text.Data("testid", "ui-editor-value").Class("font-mono text-xs break-all whitespace-pre-wrap")[
                string.IsNullOrEmpty(Notes) ? "(empty)" : Notes
            ]
        ];
}
