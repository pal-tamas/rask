namespace Rask.Core.Virtualization;

public sealed record ItemsProviderResult<T>(IReadOnlyList<T> Items, int TotalItemCount);
