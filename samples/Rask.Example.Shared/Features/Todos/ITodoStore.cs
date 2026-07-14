namespace Rask.Example.Shared.Features;

// Persistence seam for the Todos screen. The default is InMemoryTodoStore (used by the Server and WASM
// showcase, where the list is transient). The native app registers a SQLite-backed store instead, so the
// same Todos tab survives an app restart on-device — see samples/Rask.Example.Native.
public interface ITodoStore
{
    IReadOnlyList<TodoItem> GetAll();

    void Add(TodoItem item);

    void Update(TodoItem item);

    void Delete(Guid id);
}

// The transient default: a seeded in-memory list. A fresh instance per page (when nothing is injected)
// reproduces the original showcase behaviour exactly.
public sealed class InMemoryTodoStore : ITodoStore
{
    private readonly List<TodoItem> _items =
    [
        new() { Title = "Read the Rask README" },
        new() { Title = "Wire up a feature toggle", Completed = true }
    ];

    public IReadOnlyList<TodoItem> GetAll() => _items;

    public void Add(TodoItem item) => _items.Add(item);

    // Items are reference-equal to what GetAll() handed out, so an edit/toggle is already reflected;
    // nothing to persist for the in-memory store.
    public void Update(TodoItem item)
    {
    }

    public void Delete(Guid id) => _items.RemoveAll(t => t.Id == id);
}
