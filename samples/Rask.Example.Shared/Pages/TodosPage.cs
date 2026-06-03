using System.ComponentModel.DataAnnotations;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("todos")]
[Route("todos/new")]
[Route("todos/{id:guid}/edit")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class TodosPage(Navigator nav, RouteState route) : Component
{
    [RouteParam] public Guid? Id { get; set; }

    private readonly List<TodoItem> _todos =
    [
        new() { Title = "Read the Rask README" },
        new() { Title = "Wire up a feature toggle", Completed = true }
    ];

    private readonly TodoForm _form = new();

    protected override RenderResult Head => Title()["Todos — Rask"];

    private bool IsAdding => route.Path.EndsWith("/new", StringComparison.OrdinalIgnoreCase);

    private TodoItem? EditingItem =>
        Id is { } id ? _todos.FirstOrDefault(t => t.Id == id) : null;

    private bool ShowDialog => IsAdding || EditingItem is not null;

    // Fires on first render, on any [RouteParam] change, AND on URL-path change for the
    // same cached page instance (the framework OR's path change into propsChanged inside
    // RouteChainRenderer). Bare re-renders triggered by event handlers don't refire it,
    // so typing in the dialog input won't clobber what the user just typed.
    protected override void OnPropsChanged() => _form.Title = EditingItem?.Title ?? "";

    private void OpenAdd() => nav.Navigate("/todos/new");

    private void OpenEdit(TodoItem item) => nav.Navigate($"/todos/{item.Id}/edit");

    private void Cancel() => nav.Navigate("/todos");

    private void Save(TodoForm m)
    {
        var title = m.Title.Trim();
        if (IsAdding)
        {
            _todos.Add(new TodoItem { Title = title });
        }
        else if (EditingItem is { } item)
        {
            item.Title = title;
        }

        nav.Navigate("/todos");
    }

    private void Delete(TodoItem item) => _todos.Remove(item);

    protected override RenderResult Render()
    {
        var done = _todos.Count(t => t.Completed);
        return [
            PageHeader.Render(
                "Todos",
                "A small CRUD screen built on top of Rask primitives. The page declares three [Route] attributes — /todos shows the list, /todos/new opens the add dialog, /todos/{id:guid}/edit opens the edit dialog. Browser Back closes the dialog; deep links open it."),
            Div(Class: "d-flex justify-content-between align-items-center mb-3")[
                Span(Class: "text-muted small")[
                    $"{_todos.Count} item{(_todos.Count == 1 ? "" : "s")}, {done} done"
                ],
                Button("button", Class: "btn btn-primary", OnClick: OpenAdd)[
                    I(Class: "bi bi-plus-lg me-1"), "New todo"
                ]
            ],
            _todos.Count == 0
                ? Div(Class: "text-muted small")["No todos yet — click \"New todo\" to add one."]
                : Ul(Class: "list-group")[
                    _todos.Select(item => Li(Key: item.Id, Class: "list-group-item d-flex align-items-center gap-2")[
                        Input(
                            () => item.Completed,
                            Id: $"todo-done-{item.Id}",
                            Class: "form-check-input mt-0"),
                        Span(Class: item.Completed ? "todo-title completed" : "todo-title")[item.Title],
                        Button(
                            "button",
                            Class: "btn btn-outline-secondary btn-sm",
                            OnClick: () => OpenEdit(item))[
                            I(Class: "bi bi-pencil")
                        ],
                        Button(
                            "button",
                            Class: "btn btn-outline-danger btn-sm",
                            OnClick: () => Delete(item))[
                            I(Class: "bi bi-trash")
                        ]
                    ])
                ],
            TodoFormDialog(
                Open: ShowDialog,
                Model: _form,
                IsAdding: IsAdding,
                OnCancel: Cancel,
                OnSave: Save)
        ];
    }
}

public sealed class TodoFormDialog : Component
{
    public bool Open { get; set; }
    public required TodoForm Model { get; set; }
    public bool IsAdding { get; set; }
    public required Action OnCancel { get; set; }
    public required Action<TodoForm> OnSave { get; set; }

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        Fragment()[msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    protected override RenderResult Render() =>
        Dialog(Open: Open)[
            H5(Class: "mb-3")[IsAdding ? "Add todo" : "Edit todo"],
            Form(Model, OnSave, Class: "vstack gap-3")[
                DataAnnotationsValidator(),
                Div()[
                    Label("todo-title", Class: "form-label small mb-1")["Title"],
                    Input(() => Model.Title, Id: "todo-title", Class: "form-control"),
                    ValidationMessage(() => Model.Title, FieldError)
                ],
                Div(Class: "d-flex justify-content-end gap-2")[
                    Button("button", Class: "btn btn-outline-secondary", OnClick: OnCancel)["Cancel"],
                    Button("submit", Class: "btn btn-primary")[
                        I(Class: "bi bi-check2-circle me-1"),
                        IsAdding ? "Add" : "Save"
                    ]
                ]
            ]
        ];
}

public sealed class TodoItem
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
}

public sealed class TodoForm
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(120, MinimumLength = 1, ErrorMessage = "Title must be 1–120 characters.")]
    public string Title { get; set; } = "";
}
