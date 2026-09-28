namespace Rask.Site.Features;

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

    // Items are reference-equal to what GetAll() handed out, so an edit/toggle is already reflected,
    // nothing to persist for the in-memory store.
    public void Update(TodoItem item)
    {
    }

    public void Delete(Guid id) => _items.RemoveAll(t => t.Id == id);
}
