using System.ComponentModel.DataAnnotations;
using Microsoft.JSInterop;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("todos")]
[Route("todos/new")]
[Route("todos/{id:guid}/edit")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class TodosPage(Navigator nav, RouteState route) : Component
{
    private readonly TodoForm _form = new();

    private readonly List<TodoItem> _todos =
    [
        new() { Title = "Read the Rask README" },
        new() { Title = "Wire up a feature toggle", Completed = true }
    ];

    [RouteParam] public Guid? Id { get; set; }

    protected override Component? Head => Title()["Todos — Rask"];

    private bool IsAdding => route.Path.EndsWith("/new", StringComparison.OrdinalIgnoreCase);

    private TodoItem? EditingItem =>
        Id is { } id ? _todos.FirstOrDefault(t => t.Id == id) : null;

    private bool ShowDialog => IsAdding || EditingItem is not null;

    // Fires on first render, on any [RouteParam] change, AND on URL-path change for the
    // same cached page instance (the framework OR's path change into propsChanged inside
    // RouteChainRenderer). Bare re-renders triggered by event handlers don't refire it,
    // so typing in the dialog input won't clobber what the user just typed.
    protected override void OnPropsChanged() => _form.Title = EditingItem?.Title ?? "";

    private void OpenAdd() => nav.NavigateTo("/todos/new");

    private void OpenEdit(TodoItem item) => nav.NavigateTo($"/todos/{item.Id}/edit");

    private void Cancel() => nav.NavigateTo("/todos");

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

        nav.NavigateTo("/todos");
    }

    private void Delete(TodoItem item) => _todos.Remove(item);

    protected override Component? Render() =>
        [
            PageHeader.Render(
                "Todos",
                "A small CRUD screen built on top of Rask primitives. The page declares three [Route] attributes — /todos shows the list, /todos/new opens the add dialog, /todos/{id:guid}/edit opens the edit dialog. Browser Back closes the dialog; deep links open it."),
            Div(Class: "d-flex justify-content-between align-items-center mb-3")[
                Span(Class: "text-muted small")[
                    $"{_todos.Count} item{(_todos.Count == 1 ? "" : "s")}, {_todos.Count(t => t.Completed)} done"
                ],
                BsButton(Color: BsColor.Primary, OnClick: OpenAdd)[
                    BsIcon(Name: BsIconName.PlusLg, Class: "me-1"), "New todo"
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
                        BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, OnClick: () => OpenEdit(item))[
                            BsIcon(Name: BsIconName.Pencil)
                        ],
                        BsButton(Color: BsColor.Danger, Outline: true, Size: BsSize.Sm, OnClick: () => Delete(item))[
                            BsIcon(Name: BsIconName.Trash)
                        ]
                    ])
                ],
            CodeSample(
                ["TodosPage.cs"],
                Title: "Source",
                Notes:
                "The whole CRUD screen above, verbatim — page, dialog component, and model in one file. " +
                "Three [Route] attributes drive the dialog: /todos lists, /todos/new opens add, " +
                "/todos/{id:guid}/edit opens edit. OnPropsChanged seeds the form from the route so browser " +
                "Back closes the dialog and deep links open it, without clobbering in-progress typing."),
            // The open <dialog> is a viewport-centered overlay (position:fixed + high z-index, with a
            // dim backdrop) — see TodoFormDialog.css. Kept last in the DOM as a tidy belt-and-braces
            // so source order matches paint order even before the z-index applies.
            TodoFormDialog(
                ShowDialog,
                _form,
                IsAdding,
                Cancel,
                Save)
        ];
}

public sealed class TodoFormDialog : Component
{
    // A stable ref to the <dialog> so we can move focus into it when it opens — the dialog is
    // inserted by the live diff, where the HTML `autofocus` attribute never fires. Focusing it also
    // makes Escape work immediately: OnKeyDown is focus-scoped, so a key only reaches the dialog
    // while it (or a child, e.g. the title input) holds focus.
    private readonly ElementRef _dialog = ElementRef.New();
    private bool _wasOpen;

    // Focus interop injected via the ctor (the DI seam) so Model/OnCancel/OnSave stay plain factory
    // parameters. They are non-nullable + no initializer + no `required` keyword: the generator emits
    // them as required positional parameters, and Rask's post-render assignment satisfies them — so
    // CS8618 here is intentional. `required` would clash with the DI-only ctor (no parameterless ctor → RASK002). Mirrors CodeSample.
#pragma warning disable CS8618
    private readonly IJSRuntime _js;

    public TodoFormDialog(IJSRuntime js) => _js = js;

    public bool Open { get; set; }
    public TodoForm Model { get; set; }
    public bool IsAdding { get; set; }
    public Callback OnCancel { get; set; }
    public Callback<TodoForm> OnSave { get; set; }
#pragma warning restore CS8618

    private static Component FieldError(IReadOnlyList<string> msgs) =>
        [.. msgs.Select((m, i) => Div(Key: i, Class: "text-danger small mt-1")[m])];

    // Move focus into the dialog the moment it opens (false → true), so Escape closes it without a
    // prior click and a keyboard user lands inside the form. OnRenderedAsync runs after the DOM is
    // patched, so the ref resolves to the live element.
    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (Open && !_wasOpen)
        {
            await _dialog.FocusAsync(_js);
        }

        _wasOpen = Open;
    }

    // Escape cancels — the same path as the Cancel button and the backdrop click.
    private void OnKey(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            OnCancel();
        }
    }

    protected override Component? Render() =>
        [
            // Dim, clickable backdrop behind the centered dialog. A non-modal <dialog open> gets no
            // ::backdrop, so we render our own — clicking it cancels, like the nav drawer's backdrop.
            Open ? Div(Class: "todo-backdrop", OnClick: OnCancel) : null,
            // tabindex makes the <dialog> programmatically focusable; OnKeyDown gives it Escape-to-close.
            Dialog(Open, Ref: _dialog, TabIndex: -1, OnKeyDown: OnKey)[
                H5(Class: "mb-3")[IsAdding ? "Add todo" : "Edit todo"],
                Form(Model, OnSave, Class: "vstack gap-3")[
                    DataAnnotationsValidator(),
                    Div()[
                        Label("todo-title", Class: "form-label small mb-1")["Title"],
                        Input(() => Model.Title, Id: "todo-title", Class: "form-control"),
                        ValidationMessage(() => Model.Title, FieldError)
                    ],
                    Div(Class: "d-flex justify-content-end gap-2")[
                        BsButton(Color: BsColor.Secondary, Outline: true, OnClick: OnCancel)["Cancel"],
                        BsButton(Type: "submit", Color: BsColor.Primary)[
                            BsIcon(Name: BsIconName.Check2Circle, Class: "me-1"),
                            IsAdding ? "Add" : "Save"
                        ]
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
