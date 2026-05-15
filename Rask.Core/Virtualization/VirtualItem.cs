namespace Rask.Core.Virtualization;

public sealed record VirtualItem<T>(int Index, T? Value, bool IsPlaceholder);
