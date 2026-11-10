namespace Rask.Site.Features.UiDocs;

/// <summary>How a file upload behaves: the real input, dragging, progress, the files the page is handed, and previews.</summary>
internal sealed partial class UiFileUploadBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Input(),
            Dragging(),
            Files(),
            Checks(),
            Cancelling(),
            Previews()
        ];

    private static UiDocsTopic Input() =>
        UiDocsTopic.Key("input").Heading("A real file input").Paragraphs([
            "`Ui.FileUpload` is a `<label>` around a real `<input type=\"file\">`, so a click anywhere opens the picker and "
            + "the input keeps the keyboard (Space and Enter open it) with no script; the focus ring is drawn on the "
            + "dropzone. The field's label is a `<label for>`, and names the input through `aria-labelledby`, as Flux's "
            + "does, so the dropzone's words are not read out as part of its name.",
        ]);

    private static UiDocsTopic Dragging() =>
        UiDocsTopic.Key("dragging").Heading("Dragging and progress").Paragraphs([
            "While files are dragged over it the runtime writes `data-dragging` on the upload (its `data-rask-dropzone` "
            + "hook) and lays the input over the whole area, so the drop is the browser's own. A drag that carries no "
            + "files, such as a row of a sortable list, is not an offer to upload.",
            "From the moment files are chosen until `OnFiles` has rendered, the runtime writes `data-loading` and the "
            + "dropzone shows a spinner, or with `WithProgress()` a bar as wide as `--ui-file-upload-progress`, with "
            + "`--ui-file-upload-progress-as-string` beside it: Flux's two variables under the kit's prefix, `0%` at rest. "
            + "On the Server host that is the upload request's own progress; in a WebAssembly app it is how much of the "
            + "files the handler has read through `OpenReadStream`.",
        ]);

    private static UiDocsTopic Files() =>
        UiDocsTopic.Key("files").Heading("The files are the page's").Paragraphs([
            "`OnFiles` hands the page an `IRaskFile` for each file, readable only while the handler runs; its name, size "
            + "and type are the browser's word, so check them where the file lands. `Ui.FileItem` draws a row from what "
            + "the page kept: `Size` in bytes is written as B, KB, MB or GB, and `Ui.FileItemRemove` in its `Actions` "
            + "removes nothing itself.",
            "Write the remove handler where the list is: a handler re-renders the component it closes over, and one "
            + "built in a static helper that closed over the list alone would remove the file and redraw nothing.",
        ]);

    private static UiDocsTopic Checks() =>
        UiDocsTopic.Key("checks").Heading("Refused before it is sent").Paragraphs([
            "`MaxSize(2.Megabytes)` and `Accept(\"image/*\")` are checked in the browser the moment files are chosen or "
            + "dropped. A file too large, or of a kind `Accept` does not name (an ending such as `.pdf`, a whole type such "
            + "as `image/*`, or one exact type), is taken out of the choice before any of it is sent, and a sentence under "
            + "the upload names it, in the visitor's language; the dropzone is drawn red until a choice passes. The other "
            + "files chosen with it go on, and `OnFiles` hears only those. The server's own limits still stand behind it.",
        ]);

    private static UiDocsTopic Cancelling() =>
        UiDocsTopic.Key("cancelling").Heading("Calling an upload off").Paragraphs([
            "With `WithProgress()` the dropzone draws a cross beside its bar while files go. On the Server host it stops "
            + "the request still carrying them, and the page hears nothing of them; in a WebAssembly app it cancels the "
            + "handler reading them, whose `Ambient` cancellation a save it awaits honours. The input is emptied, so "
            + "choosing the same file again starts again. Files that have all arrived on the Server host and whose "
            + "handler is running are past calling off.",
        ]);

    private static UiDocsTopic Previews() =>
        UiDocsTopic.Key("previews").Heading("Previews").Paragraphs([
            "`Ui.FileItem.Preview(file)`, with the `IRaskFile` the handler received, shows a chosen picture at once: the "
            + "browser draws it from the file it still holds, and no byte is sent or stored for it. The address it uses "
            + "lives exactly as long as the picture is on the page. A file that is no image keeps the icon, and so does "
            + "one this tab did not see chosen (after a reload).",
            "For a picture already saved, hand `Ui.FileItem.Image` the address it was stored under, or read it in "
            + "`OnFiles` into a `data:` address as the first example does: take the type from the browser only for the "
            + "few an `<img>` draws, and cap the size.",
        ]);
}
