namespace Rask.Site.Features;

public sealed partial class TodoFormDialog : Component
{
    // Non-nullable props with no initializer → the generator emits them as required positional factory
    // parameters (RASK001); Rask's post-render assignment satisfies them, so CS8618 here is intentional.
    // The dialog is driven entirely by route state, so no IJSRuntime/ElementRef is
    // needed — the whole dialog is one composed component tree with no lifecycle plumbing.
#pragma warning disable CS8618
    public bool Open { get; set; }
    public TodoForm Model { get; set; }
    public bool IsAdding { get; set; }
    public Callback OnCancel { get; set; }
    public Callback<TodoForm> OnSave { get; set; }
#pragma warning restore CS8618

    // OnClose fires for Escape, a backdrop click, and the header close button — all route back to /todos
    // via OnCancel, which flips ShowDialog and closes the modal. Browser Back does the same through the URL.
    // Template is required, so it is the chain's opening step: a validation message with no way to
    // render itself is not a thing the type system lets you ask for.
    private static Component FieldError(IReadOnlyList<string> errors) =>
        Div.Class("field-error text-sm text-ui-danger-ink")[errors.Select(e => Div.Key(e)[e])];

    protected override Component? Render() =>
        // The native <dialog>. BsModal supplied a backdrop, Escape-to-dismiss and a focus trap. A
        // <dialog> rendered with the `open` attribute is NON-modal, so it supplies none of the three —
        // showModal() would, but it needs JS. The first two are cheap to keep as Rask state and a
        // dialog without them is a worse dialog, so they are rebuilt below. The true focus TRAP (tab
        // cannot leave) is the part that genuinely needs showModal, and is the honest reduction.
        [
            // A non-modal <dialog open> paints no backdrop of its own, so without this there is nothing
            // dimming the page and nothing to click outside the dialog. It carries the click that cancels.
            !Open
                ? null
                : Div.Class("dialog-backdrop fixed inset-0 z-40 bg-black/40").OnClick(OnCancel),
            Dialog.Open(Open).Class(
                "fixed inset-0 z-50 m-auto h-fit w-full max-w-md rounded-xl bg-ui-bg p-5 shadow-xl")
                // Escape dismisses. A non-modal dialog fires no `cancel` event, so the key is read
                // where it lands — no client script, just the same routed cancel the backdrop uses.
                .OnKeyDown(async e =>
                {
                    if (e.Key is "Escape")
                    {
                        await OnCancel.Invoke();
                    }
                })[
                H2.Class("mb-3 text-lg font-semibold")[IsAdding ? "Add todo" : "Edit todo"],
                Form.Model(Model).OnSubmit(OnSave).Class("flex flex-col gap-3")[
                    // autofocus fires when the browser PARSES the element -- a deep link to /todos/new
                    // lands in the field. Opening the dialog through the live diff inserts it after
                    // parse, where browsers ignore the attribute, so that path still needs a click.
                    // Reliable focus-on-open would need ElementRef + IJSRuntime; this page is a routed
                    // CRUD flow, not a dialog implementation.
                    Ui.Input.Bind(() => Model.Title).Label("Title").Id("todo-title").Autofocus(true).ShowValidation(false),
                    Validation.Message.Template(FieldError).For(() => Model.Title),
                    Div.Class("flex justify-end gap-2")[
                        Ui.Button.Outline.OnClick(OnCancel)["Cancel"],
                        Ui.Button
                            .Primary
                            .Submit[Ui.Icon.Name(Ui.IconName.CheckCircle), IsAdding ? "Add" : "Save"]
                    ]
                ]
            ]
        ];
}
