using System.ComponentModel.DataAnnotations;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("todos")]
[Route("todos/new")]
[Route("todos/{id:guid}/edit")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class TodosPage : Component
{
    private readonly Navigator _nav;
    private readonly RouteState _route;
    private readonly TodoForm _form = new();

    // Persistence seam: the injected ITodoStore, or a throwaway seeded in-memory store when none is
    // registered (Server/WASM showcase). An app can register a durable store — e.g. SQLite-backed — and the
    // same screen then persists across a restart. _todos is the render working set, written through to the
    // store on every change.
    private readonly ITodoStore _store;

    private readonly List<TodoItem> _todos;

    public TodosPage(Navigator nav, RouteState route, ITodoStore? store = null)
    {
        _nav = nav;
        _route = route;
        _store = store ?? new InMemoryTodoStore();
        _todos = _store.GetAll().ToList();
    }

    [RouteParam] public Guid? Id { get; set; }

    protected override Component? HeadAssets => Title["Todos — Rask"];

    private bool IsAdding => _route.Path.EndsWith("/new", StringComparison.OrdinalIgnoreCase);

    private TodoItem? EditingItem =>
        Id is { } id ? _todos.FirstOrDefault(t => t.Id == id) : null;

    private bool ShowDialog => IsAdding || EditingItem is not null;

    // Fires on first render, on any [RouteParam] change, AND on URL-path change for the
    // same cached page instance (the framework OR's path change into propsChanged inside
    // RouteChainRenderer). Bare re-renders triggered by event handlers don't refire it,
    // so typing in the dialog input won't clobber what the user just typed.
    protected override void OnPropsChanged() => _form.Title = EditingItem?.Title ?? "";

    // The list route has a generated type-safe URL (Routes.TodosPage() → "/todos"); the /new and
    // /{id}/edit dialog routes are secondary [Route] templates on this same page, which the generator
    // doesn't emit a formatter for, so those two stay as string paths.
    private void OpenAdd() => _nav.NavigateTo("/todos/new");

    private void OpenEdit(TodoItem item) => _nav.NavigateTo($"/todos/{item.Id}/edit");

    private void Cancel() => _nav.NavigateTo(Routes.TodosPage());

    // Every mutation is written through to the store, so a SQLite-backed store (native) persists it.
    private void Save(TodoForm m)
    {
        var title = m.Title.Trim();
        if (IsAdding)
        {
            var item = new TodoItem { Title = title };
            _todos.Add(item);
            _store.Add(item);
        }
        else if (EditingItem is { } item)
        {
            item.Title = title;
            _store.Update(item);
        }

        _nav.NavigateTo(Routes.TodosPage());
    }

    private void Toggle(TodoItem item, bool completed)
    {
        item.Completed = completed;
        _store.Update(item);
    }

    private void Delete(TodoItem item)
    {
        _todos.Remove(item);
        _store.Delete(item.Id);
    }

    protected override Component? Render() =>
        [
            PageHeader
                .Title("Todos")
                .Lead("A small CRUD screen built on top of Rask primitives. The page declares three [Route] attributes — /todos shows the list, /todos/new opens the add dialog, /todos/{id:guid}/edit opens the edit dialog. Browser Back closes the dialog; deep links open it."),
            Div
                .Class("flex justify-between items-center mb-3")[
                Span.Class("text-ui-muted text-sm")[
                    $"{_todos.Count} item{(_todos.Count == 1 ? "" : "s")}, {_todos.Count(t => t.Completed)} done"
                ],
                Button.Type("button").Class(Tw.BtnPrimary).OnClick(OpenAdd)[
                    UiIcon.Name(UiIconName.Plus).Class("me-1"), "New todo"
                ]
            ],
            _todos.Count == 0
                ? Div.Class("text-ui-muted text-sm")["No todos yet — click \"New todo\" to add one."]
                : Ul.Id("todo-list")
                    .Class("divide-y divide-ui-line rounded-lg ring-1 ring-ui-line")[
                    _todos.Select(item => Li
                        .Key(item.Id)
                        .Class("flex items-center gap-2 px-3 py-2")[
                        // Input derives type="checkbox" from the bool it is given — there is no
                        // separate checkbox control to reach for.
                        Input
                            .Value(item.Completed)
                            .OnChange(v => Toggle(item, v))
                            .Id($"todo-done-{item.Id}")
                            .Class("size-4"),
                        Span.Class(item.Completed ? "todo-title completed" : "todo-title")[item.Title],
                        // Icon-only, so the glyph is the whole button: without an accessible name a
                        // screen reader announces "button" and nothing else. Bootstrap Icons carried no
                        // name either -- the label is what the icon was always standing in for.
                        Button.Type("button").Class(Tw.BtnOutlineSecondary)
                            .Aria(new Dictionary<string, string?> { ["label"] = $"Edit {item.Title}" })
                            .OnClick(() => OpenEdit(item))[
                            UiIcon.Name(UiIconName.Pencil)
                        ],
                        Button.Type("button").Class(Tw.BtnOutlineDanger)
                            .Aria(new Dictionary<string, string?> { ["label"] = $"Delete {item.Title}" })
                            .OnClick(() => Delete(item))[
                            UiIcon.Name(UiIconName.Trash)
                        ]
                    ])
                ],
            CodeSample
                .Files(["TodosPage.cs"])
                .Title("Source")
                .Notes("The whole CRUD screen above, verbatim — page, dialog component, and model in one file. " +
                "Three [Route] attributes drive the dialog: /todos lists, /todos/new opens add, " +
                "/todos/{id:guid}/edit opens edit. OnPropsChanged seeds the form from the route so browser " +
                "Back closes the dialog and deep links open it, without clobbering in-progress typing."),
            // A dialog driven by the route: Open follows ShowDialog, and
            // Escape / backdrop-click / the header close button all route back to /todos via OnCancel.
            TodoFormDialog
                .Open(ShowDialog)
                .Model(_form)
                .IsAdding(IsAdding)
                .OnCancel(Cancel)
                .OnSave(Save)
        ];
}

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
    public Action OnCancel { get; set; }
    public Action<TodoForm> OnSave { get; set; }
#pragma warning restore CS8618

    // OnClose fires for Escape, a backdrop click, and the header close button — all route back to /todos
    // via OnCancel, which flips ShowDialog and closes the modal. Browser Back does the same through the URL.
    // Template is required, so it is the chain's opening step: a validation message with no way to
    // render itself is not a thing the type system lets you ask for.
    private static Component FieldError(IReadOnlyList<string> errors) =>
        Div.Class("field-error text-sm text-ui-danger")[errors.Select(e => Div.Key(e)[e])];

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
                : Div.Class("dialog-backdrop fixed inset-0 z-40 bg-black/40").OnClick(() => OnCancel?.Invoke()),
            Dialog.Open(Open).Class(
                "fixed inset-0 z-50 m-auto h-fit w-full max-w-md rounded-xl bg-ui-bg p-5 shadow-xl")
                // Escape dismisses. A non-modal dialog fires no `cancel` event, so the key is read
                // where it lands — no client script, just the same routed cancel the backdrop uses.
                .OnKeyDown(e =>
                {
                    if (e.Key == "Escape")
                    {
                        OnCancel?.Invoke();
                    }
                })[
                H2.Class("mb-3 text-lg font-semibold")[IsAdding ? "Add todo" : "Edit todo"],
                Form.Model(Model).OnValidSubmit(OnSave).Class("flex flex-col gap-3")[
                    DataAnnotationsValidator,
                    Label.For("todo-title").Class("text-sm font-medium")["Title"],
                    // autofocus fires when the browser PARSES the element -- a deep link to /todos/new
                    // lands in the field. Opening the dialog through the live diff inserts it after
                    // parse, where browsers ignore the attribute, so that path still needs a click.
                    // Reliable focus-on-open would need ElementRef + IJSRuntime; this page is a routed
                    // CRUD flow, not a dialog implementation.
                    Input.Bind(() => Model.Title).Id("todo-title").Autofocus(true).Class(Tw.Input),
                    ValidationMessage.Template(FieldError).For(() => Model.Title),
                    Div.Class("flex justify-end gap-2")[
                        Button.Type("button").Class(Tw.BtnOutlineSecondary).OnClick(OnCancel)["Cancel"],
                        Button.Class(Tw.BtnPrimary).Type("submit")[
                            UiIcon.Name(UiIconName.CheckCircle).Class("me-1"),
                            IsAdding ? "Add" : "Save"
                        ]
                    ]
                ]
            ]
        ];
}

public sealed class TodoItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}

public sealed class TodoForm
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(120, MinimumLength = 1, ErrorMessage = "Title must be 1–120 characters.")]
    public string Title { get; set; } = "";
}
