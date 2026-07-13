using System.Runtime.CompilerServices;

namespace Rask.Core.Forms;

// Bridges a form control's creation site to its Render. Records the component that CREATED a form
// control — the "binding consumer" / provider: the component whose Render() authored the control and
// (typically) any derived UI beside it. A bound two-way write then re-renders that provider even when
// the control is a wrapper Component (BsCheck/BsInput/…) that would otherwise absorb the re-render into
// itself, and even when the bind expression closed over a loop local (e.g. `() => item.Completed`),
// where ExpressionAccessor can't recover the authoring component from the expression root.
//
// Keyed WEAKLY by the control instance (ConditionalWeakTable), so an entry disappears with its control
// and no per-node field is added to Component/Element — only the handful of live form controls hold an
// entry, keeping the render-node footprint unchanged.
internal static class BindingConsumerRegistry
{
    private static readonly ConditionalWeakTable<object, Component> _creators = new();

    // Called from Component.GetOrCreateChild the moment a form control is created/reused, where the
    // caller (`this`) IS the creating parent (CurrentParent during the provider's Render).
    public static void Record(object control, Component creator) =>
        _creators.AddOrUpdate(control, creator);

    public static Component? Resolve(object control) =>
        _creators.TryGetValue(control, out var creator) ? creator : null;
}
