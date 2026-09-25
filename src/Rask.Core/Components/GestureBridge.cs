using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Rask.Core.Browser;

namespace Rask.Core.Components;

internal static class GestureBridge
{
    // Roots GestureResultInterop's [JSInvokable] for the WASM trimmer — it's reached only via the JS
    // DotNet dispatcher (reflection), so without this the Result method could be trimmed away.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(GestureResultInterop))]
    public static IReadOnlyDictionary<string, string?> Attr(
        string capability, Callback<string?>? onResult, string? arg = null, string? el = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(capability);

        // Adapted once, here, rather than at each of the six call sites: the interop registry
        // dispatches a task-returning delegate, and the carrier may be holding either shape.
        var rid = onResult is null
            ? (int?)null
            : GestureResultInterop.Register(v => onResult.Value.Invoke(v));
        var json = JsonSerializer.Serialize(
            new GesturePayload(capability, rid, arg, el), RaskBrowserJsonContext.Default.GesturePayload);
        return new Dictionary<string, string?>(StringComparer.Ordinal) { ["rask-gesture"] = json };
    }
}
