using Rask.Core;

namespace Rask.DevTools.Probe;

/// <summary>One component as the walk met it: what it is, what it was walked inside, and what it wrote.</summary>
internal readonly record struct DevToolsWalkItem(Component Component, Component? Parent, int FrameStart, int FrameEnd);
