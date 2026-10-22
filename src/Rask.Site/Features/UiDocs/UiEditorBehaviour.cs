namespace Rask.Site.Features.UiDocs;

/// <summary>How an editor behaves: its value, the engine, the toolbar's items and keys, Markdown, and extensions.</summary>
internal sealed partial class UiEditorBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Engine(),
            Items(),
            Extensions()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("The value is the reader's HTML").Paragraphs([
            "The value is the document as HTML, `<p>Hello <strong>world</strong></p>`, and an empty document is the empty "
            + "string. `Bind` writes it with the next action (`.Live()`: on every change) and validates the field then; "
            + "`Value` with `OnChange` leaves it to you. A value the app changes afterwards is shown in the editor.",
            "The editor itself only produces the tags of its schema, but what you bind may have come from anywhere, and "
            + "the editor writes its value into the page as it is, exactly as `Raw` does. Sanitize HTML on the server "
            + "before you store it or bind it, and never render a stored value as markup without doing so. Shown as text, "
            + "as the first example shows it, it is always safe.",
        ]);

    private static UiDocsTopic Engine() =>
        UiDocsTopic.Key("engine").Heading("Nothing to configure").Paragraphs([
            "The engine is 117 KB gzipped, so it is in neither Rask's runtime nor the kit's assembly: it is a static file, "
            + "`wwwroot/js/rask-ui-editor.js`, which the build of every app that references the kit writes, and which the "
            + "browser fetches the first time an editor mounts. A page without an editor never requests it. An app that "
            + "will never draw one keeps it out of its publish with `<RaskUiEditorEngine>false</RaskUiEditorEngine>`.",
            "Until the script has loaded, and with scripting off, the editor shows its value as plain markup. A strict "
            + "`Content-Security-Policy` needs nothing added: the file is same-origin script. Its notices (Tiptap and "
            + "ProseMirror, MIT; eleven Lucide icons, ISC) are `rask-ui-editor.LICENSES.txt`, written beside it.",
        ]);

    private static Component Items() =>
        UiDocsTopic.Key("items").Heading("The toolbar and its keys").Paragraphs([
            "The default is `heading | bold italic strike | bullet ordered blockquote | link | align`. The toolbar is one "
            + "tab stop and the arrow keys walk its controls. `Cmd` on a Mac. Markdown works while typing: `#`, `##`, "
            + "`###`, `**bold**`, `*italic*`, `~~strike~~`, `-`, `1.`, `>`, a backtick for code, three for a code block "
            + "and `---` for a rule.",
        ])[
            UiDocsKeys.Column("Item").Rows([
                ("`heading`", "Text, or a heading of level 1 to 3, from a list. `Ctrl`+`Alt`+`0` to `3`."),
                ("`bold` `italic` `strike` `underline`", "The mark. `Ctrl`+`B`, `I`, `Shift`+`S`, `U`."),
                ("`subscript` `superscript` `highlight` `code`", "The mark. `Ctrl`+`,`, `.`, `Shift`+`H`, `E`."),
                ("`bullet` `ordered` `blockquote`", "The block. `Ctrl`+`Shift`+`8`, `7`, `B`."),
                ("`link`", "A panel with the address, a button to set it and one to remove it. `Ctrl`+`K`."),
                ("`align`", "Left, center or right, from a list. `Ctrl`+`Shift`+`L`, `E`, `R`."),
                ("`undo` `redo`", "History. `Ctrl`+`Z`, `Ctrl`+`Shift`+`Z`."),
            ])
        ];

    private static UiDocsTopic Extensions() =>
        UiDocsTopic.Key("extensions").Heading("Extensions").Paragraphs([
            "Highlight, Link, Placeholder, StarterKit, Subscript, Superscript, TextAlign and Underline are on; Table, "
            + "TableRow, TableCell and TableHeader are bundled and off. Before an editor is created it raises `ui:editor` "
            + "on itself, Flux's `flux:editor` event under the kit's name, and a script of the page can call "
            + "`enableExtension`, `disableExtension`, `registerExtensions` and `init` on its `detail`, as on Flux.",
            "The editor's root has what Flux's has: `element.value`, to get and set, and the `editor` instance, and it "
            + "raises `input` and `change` for every change of the document.",
        ]);
}
