using Rask.Core;
using Rask.Core.Diagnostics.DevTools;

namespace Rask.DevTools.Probe;

/// <summary>A render the walk in progress ran, before the commit it belongs to is known.</summary>
internal record struct DevToolsRenderItem(Component Component, RenderCause Cause, long SelfTicks);
