namespace Rask.Core.Components;

// Type-erased ItemsProvider result. The typed VirtualizeModel<T>(...) factory boxes the user's
// IReadOnlyList<T> into IReadOnlyList<object?> before handing the closure off to the
// non-generic component. Kept internal — users only see the typed ItemsProviderResult<T>.
internal sealed record ItemsProviderResultErased(IReadOnlyList<object?> Items, int TotalItemCount);
