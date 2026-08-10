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
    // registered (Server/WASM showcase). The native app registers a SQLite-backed store, so the same
    // screen persists across an app restart on-device. _todos is the render working set, written through
    // to the store on every change.
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

    protected override Component? Head => Title["Todos — Rask"];

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
                .Class(Bs.Join(Display.Flex(), Flex.Justify(BsJustify.Between), Flex.Align(BsAlign.Center),
                Margin.Bottom(3)))[
                Span.Class(Bs.Join(Txt.Muted, Font.Small))[
                    $"{_todos.Count} item{(_todos.Count == 1 ? "" : "s")}, {_todos.Count(t => t.Completed)} done"
                ],
                BsButton.Color(BsColor.Primary).OnClick(OpenAdd)[
                    BsIcon.Name(BsIconName.PlusLg).Class(Margin.End(1)), "New todo"
                ]
            ],
            _todos.Count == 0
                ? Div.Class(Bs.Join(Txt.Muted, Font.Small))["No todos yet — click \"New todo\" to add one."]
                : BsListGroup[
                    _todos.Select(item => BsListGroupItem
                        .Key(item.Id)
                        .Class(Bs.Join(Display.Flex(), Flex.Align(BsAlign.Center), Flex.Gap(2)))[
                        BsCheck
                            .Value(item.Completed)
                            .OnChange(v => Toggle(item, v))
                            .Id($"todo-done-{item.Id}")
                            .Class(Margin.Bottom(0)),
                        Span.Class(item.Completed ? "todo-title completed" : "todo-title")[item.Title],
                        BsButton
                            .Color(BsColor.Secondary)
                            .Outline(true)
                            .Size(BsSize.Sm)
                            .OnClick(() => OpenEdit(item))[
                            BsIcon.Name(BsIconName.Pencil)
                        ],
                        BsButton.Color(BsColor.Danger).Outline(true).Size(BsSize.Sm).OnClick(() => Delete(item))[
                            BsIcon.Name(BsIconName.Trash)
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
            // A BsModal (zero-JS Bootstrap modal) driven by the route: Open follows ShowDialog, and
            // Escape / backdrop-click / the header close button all route back to /todos via OnCancel.
            TodoFormDialog
                .Open(ShowDialog)
                .Model(_form)
                .IsAdding(IsAdding)
                .Cancel(Cancel)
                .Save(Save)
        ];
}

public sealed partial class TodoFormDialog : Component
{
    // Non-nullable props with no initializer → the generator emits them as required positional factory
    // parameters (RASK001); Rask's post-render assignment satisfies them, so CS8618 here is intentional.
    // BsModal supplies the focus trap, Escape-to-dismiss, and backdrop, so no IJSRuntime/ElementRef is
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
    protected override Component? Render() =>
        BsModal.Open(Open).Title(IsAdding ? "Add todo" : "Edit todo").Centered(true).OnClose(OnCancel)[
            Form(Model, OnSave, Class: Bs.Join(Display.Flex(), Flex.Column(), Flex.Gap(3)))[
                DataAnnotationsValidator,
                // BsInput renders its own <label> + input + Bootstrap .invalid-feedback from the
                // EditContext, so the raw Label/Input/ValidationMessage trio collapses to one call.
                BsInput(() => Model.Title).Id("todo-title").Label("Title"),
                Div.Class(Bs.Join(Display.Flex(), Flex.Justify(BsJustify.End), Flex.Gap(2)))[
                    BsButton.Color(BsColor.Secondary).Outline(true).OnClick(OnCancel)["Cancel"],
                    BsButton.Type("submit").Color(BsColor.Primary)[
                        BsIcon.Name(BsIconName.Check2Circle).Class(Margin.End(1)),
                        IsAdding ? "Add" : "Save"
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
