using Rask.Core;

namespace Rask.DevTools.Probe;

/// <summary>One read as the walk met it: the component that read, what it asked for, and whose provider answered.</summary>
internal readonly record struct DevToolsReadItem(
    Component Reader, Type Requested, string? Name, bool Found, Component? Provider);
