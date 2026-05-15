namespace Rask.Core.Virtualization;

public sealed record ItemsProviderRequest(int StartIndex, int Count, CancellationToken CancellationToken);
