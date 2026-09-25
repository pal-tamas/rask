using Rask.Core;

namespace Rask.DevTools.Probe;

/// <summary>One provide as the walk met it: the component whose markup holds the provider, and what it provided.</summary>
internal readonly record struct DevToolsProvideItem(Component Owner, Type ValueType, string? Name, object? Value);
