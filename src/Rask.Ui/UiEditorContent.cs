namespace Rask;

/// <summary>
///     The editable area of an editor, Flux's <c>flux:editor.content</c>. Placed by hand only when the editor is
///     composed; what is inside it is the HTML the editor starts with.
/// </summary>
/// <remarks>
///     <code>
///     Ui.Editor.Bind(() => _post.Body)[Ui.EditorToolbar[…], Ui.EditorContent]
///     </code>
///     The area is 200px to 500px tall and scrolls beyond that; an app sets its own limits on
///     <c>[data-slot=content]</c>: <c>Ui.Editor.Class("**:data-[slot=content]:min-h-[100px]!")</c>.
/// </remarks>
public sealed partial class UiEditorContent : Component
{
    private const string Look =
        "min-h-[200px] max-h-[500px] overflow-y-auto p-4 text-sm whitespace-pre-wrap text-zinc-700 "
        + "dark:text-zinc-300 in-aria-disabled:text-zinc-500 dark:in-aria-disabled:text-zinc-400";

    /// <inheritdoc />
    protected override Component? Render()
    {
        var editor = Context.Get<UiEditorScope>();
        var composed = (Children ?? []).Where(child => child is not null).ToList();
        var surface = Div.Class(Look).Data("slot", "content").Role("textbox");
        if (editor is not null)
        {
            surface = surface.Id(editor.InputId);
        }

        // Below the host the DOM is ProseMirror's: every keystroke rewrites it, so Rask never patches into it.
        return HostedElement.Tag("div").Opaque(true)[surface[composed.Count != 0 ? composed : [Initial(editor)]]];
    }

    // What the engine parses when it takes over: the app's HTML, or the empty paragraph ProseMirror itself
    // draws for an empty document, so the first paint already shows the placeholder.
    private static Component Initial(UiEditorScope? editor)
    {
        if (!string.IsNullOrEmpty(editor?.Value))
        {
            return Raw.Value(editor.Value);
        }

        var empty = P.Class("is-empty is-editor-empty");
        return (editor?.Placeholder is { } placeholder ? empty.Data("placeholder", placeholder) : empty)[
            Br.Class("ProseMirror-trailingBreak")
        ];
    }
}
