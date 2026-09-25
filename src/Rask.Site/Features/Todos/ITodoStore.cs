namespace Rask.Site.Features;

// Persistence seam for the Todos screen. The default is InMemoryTodoStore (used by the Server and WASM
// showcase, where the list is transient). A host can register a durable store instead, so the same Todos
// tab survives a restart.
public interface ITodoStore
{
    IReadOnlyList<TodoItem> GetAll();

    void Add(TodoItem item);

    void Update(TodoItem item);

    void Delete(Guid id);
}
