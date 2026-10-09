using Rask.Core.Routing;

namespace Rask.Site.Features;

[Route("todos")]
[Route("todos/new")]
[Route("todos/{id:guid}/edit")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed partial class TodosPage : Component
{
    private readonly RouteState _route;
    private readonly TodoForm _form = new();

    // Persistence seam: the injected ITodoStore, or a throwaway seeded in-memory store when none is
    // registered (Server/WASM showcase). An app can register a durable store — e.g. SQLite-backed — and the
    // same screen then persists across a restart. _todos is the render working set, written through to the
    // store on every change.
    private readonly ITodoStore _store;

    private readonly List<TodoItem> _todos;

    public TodosPage(RouteState route, ITodoStore? store = null)
    {
        _route = route;
        _store = store ?? new InMemoryTodoStore();
        _todos = _store.GetAll().ToList();
    }

    [RouteParam] public Guid? Id { get; set; }

    protected override Component? HeadAssets =>
        PageMeta.For(
            "C# CRUD example with routing and forms — Rask",
            "A to-do list CRUD example in C#: list, add, edit, complete and delete, with route-driven "
            + "dialogs, form validation and the full source, running in WebAssembly.",
            Routes.TodosPage());

    // Trimmed first: the host serves /docs/todos/new/, which is the path a reload — and now a link — arrives
    // with, and "/new/" does not end with "/new".
    private bool IsAdding => _route.Path.TrimEnd('/').EndsWith("/new", StringComparison.OrdinalIgnoreCase);

    private TodoItem? EditingItem =>
        Id is { } id ? _todos.FirstOrDefault(t => t.Id == id) : null;

    private bool ShowDialog => IsAdding || EditingItem is not null;

    // Fires on first render, on any [RouteParam] change, AND on URL-path change for the
    // same cached page instance (the framework OR's path change into propsChanged inside
    // RouteChainPages). Bare re-renders triggered by event handlers don't refire it,
    // so typing in the dialog input won't clobber what the user just typed.
    protected override async Task OnUpdated() => _form.Title = EditingItem?.Title ?? "";

    // The list route has a generated type-safe URL; the /new and /{id}/edit dialog routes are secondary
    // [Route] templates on this same page, and the generator emits no formatter for those — so they are
    // built by appending to the generated one rather than written out.
    //
    // They used to be the literals "/todos/new" and "/todos/{id}/edit", which was fine while this page
    // sat at the app root. Under a [ParentRoute] the real URL is /docs/todos/new, so the literals
    // navigated to a path with no route behind it: the dialog never opened and the page rendered the
    // 404. Deriving them from Routes.TodosPage() keeps the secondary templates pinned to the primary.
    private static void OpenAdd() => Go.To($"{Routes.TodosPage()}/new");

    private static void OpenEdit(TodoItem item) => Go.To($"{Routes.TodosPage()}/{item.Id}/edit");

    private static void Cancel() => Go.To(Routes.TodosPage());

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

        Go.To(Routes.TodosPage());
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

    private Component TodoRow(TodoItem item) =>
        Li
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
            // screen reader announces "button" and nothing else. An icon carries no
            // name of its own -- the label is what it stands in for.
            // The Aria step is gone because the Label IS the accessible name here: a
            // square button holds one glyph, so Ui.Button writes the label as aria-label
            // rather than as visible text.
            Ui.Button.Icon(Ui.IconName.Pencil)
                .AriaLabel($"Edit {item.Title}")
                .OnClick(() => OpenEdit(item)),
            Ui.Button.Red.Icon(Ui.IconName.Trash)
                .AriaLabel($"Delete {item.Title}")
                .OnClick(() => Delete(item))
        ];

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
                Ui.Button.Primary.Icon(Ui.IconName.Plus).OnClick(OpenAdd)["New todo"]
            ],
            _todos.Count == 0
                ? Div.Class("text-ui-muted text-sm")["No todos yet — click \"New todo\" to add one."]
                : Ul.Id("todo-list")
                    .Class("divide-y divide-ui-line rounded-lg ring-1 ring-ui-line")[
                    _todos.Select(TodoRow)
                ],
            CodeSample
                .Files(["TodosPage.cs", "TodoFormDialog.cs", "TodoItem.cs", "TodoForm.cs"])
                .Title("Source")
                .Notes("The whole CRUD screen above, verbatim — the page, its dialog component, and the models, a file each. " +
                "Three [Route] attributes drive the dialog: /todos lists, /todos/new opens add, " +
                "/todos/{id:guid}/edit opens edit. Updated seeds the form from the route so browser " +
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
