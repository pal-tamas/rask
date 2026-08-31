using System.ComponentModel.DataAnnotations;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("todos")]
[Route("todos/new")]
[Route("todos/{id:guid}/edit")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class TodosPage : Component
{
    // Icon-only buttons need their name here: Icon renders aria-hidden, so without these a screen
    // reader announces two unlabelled buttons per row. They double as the E2E locators, which used to
    // reach for the Bootstrap icon class that no longer exists.
    private static readonly IReadOnlyDictionary<string, string?> EditAria =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = "Edit todo" };

    private static readonly IReadOnlyDictionary<string, string?> DeleteAria =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = "Delete todo" };

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
                Span.Class("text-slate-500 dark:text-slate-400 text-sm")[
                    $"{_todos.Count} item{(_todos.Count == 1 ? "" : "s")}, {_todos.Count(t => t.Completed)} done"
                ],
                Button.Type("button").Class(Ui.BtnPrimary).OnClick(OpenAdd)[
                    Icon.Name(IconName.PlusLg).Class("me-1"), "New todo"
                ]
            ],
            _todos.Count == 0
                ? Div.Class("text-slate-500 dark:text-slate-400 text-sm")["No todos yet — click \"New todo\" to add one."]
                // todo-list / todo-item are TEST contracts, like todo-title below: the journey counts
                // rows through them. They used to be Bootstrap's list-group classes, which vanished
                // with the package and took the locators with them.
                : Ul.Class("todo-list divide-y divide-slate-200 rounded-lg ring-1 ring-slate-200 dark:divide-slate-700 dark:ring-slate-700")[
                    _todos.Select(item => Li
                        .Key(item.Id)
                        .Class("todo-item flex items-center gap-2 px-3 py-2")[
                        // Input derives type="checkbox" from the bool it is given — there is no
                        // separate checkbox control to reach for.
                        Input
                            .Value(item.Completed)
                            .OnChange(v => Toggle(item, v))
                            .Id($"todo-done-{item.Id}")
                            .Class("size-4"),
                        Span.Class(item.Completed ? "todo-title completed" : "todo-title")[item.Title],
                        // Icon-only, so the glyph is the whole label — and Icon is aria-hidden, which
                        // leaves these two buttons announced as "button" and nothing else. The name
                        // has to come from the button itself.
                        Button.Type("button").Class(Ui.BtnOutlineSecondary)
                            .Aria(EditAria)
                            .OnClick(() => OpenEdit(item))[
                            Icon.Name(IconName.Pencil)
                        ],
                        Button.Type("button").Class(Ui.BtnOutlineDanger)
                            .Aria(DeleteAria)
                            .OnClick(() => Delete(item))[
                            Icon.Name(IconName.Trash)
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
        // invalid-feedback is the showcase's one validation-error contract (the Floating* wrappers
        // use it too). It carries no styling of its own any more -- the utilities beside it do.
        Div.Class("invalid-feedback text-sm text-red-600 dark:text-red-400")[
            errors.Select(e => Div.Key(e)[e])];

    protected override Component? Render() =>
        // The native <dialog>. BsModal supplied a focus trap, Escape-to-dismiss and a backdrop; a
        // <dialog> rendered with the `open` attribute is NON-modal, so it gets the backdrop from the
        // sibling overlay below and loses the focus trap. That is a real reduction, and the honest
        // one: the trap needs showModal(), which needs JS, and this page exists to show a routed CRUD
        // flow rather than to reimplement a dialog.
        [
        // The backdrop. Dismisses on click, which is the only way out besides Cancel now that Escape
        // is gone with the modal-ness — without it the dialog was a trap with one exit. app-dialog /
        // dialog-backdrop are TEST contracts: the journey opens the dialog and cancels through them.
        !Open
            ? null
            : Div
                .Class("dialog-backdrop fixed inset-0 z-40 bg-black/40")
                .OnClick(OnCancel),
        Dialog.Open(Open).Class(
            "app-dialog fixed inset-0 z-50 m-auto h-fit w-full max-w-md rounded-xl bg-white p-5 "
            + "shadow-xl dark:bg-slate-800")[
            H2.Class("mb-3 text-lg font-semibold")[IsAdding ? "Add todo" : "Edit todo"],
            Form.Model(Model).OnValidSubmit(OnSave).Class("flex flex-col gap-3")[
                DataAnnotationsValidator,
                Label.For("todo-title").Class("text-sm font-medium")["Title"],
                Input.Bind(() => Model.Title).Id("todo-title").Class(Ui.Input),
                ValidationMessage.Template(FieldError).For(() => Model.Title),
                Div.Class("flex justify-end gap-2")[
                    Button.Type("button").Class(Ui.BtnOutlineSecondary).OnClick(OnCancel)["Cancel"],
                    Button.Class(Ui.BtnPrimary).Type("submit")[
                        Icon.Name(IconName.Check2Circle).Class("me-1"),
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
