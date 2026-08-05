using System.Reflection.Metadata;
using Rask.Core.HotReload;

[assembly: MetadataUpdateHandler(typeof(RaskHotReloadHandler))]

namespace Rask.Core.HotReload;

/// <summary>
///     Rask's single entry point from C# Hot Reload. The runtime calls
///     <see cref="UpdateApplication" /> after applying new IL under <c>dotnet watch</c>; a normal or
///     published run never invokes a <c>MetadataUpdateHandler</c> at all, so this costs production
///     nothing.
///     <para>
///         There is deliberately exactly one handler. The runtime does not define the order in which
///         it invokes multiple handlers, and Rask's phases are order-dependent — the scoped-asset
///         and registry refreshes must complete before any session repaints, or the frame ships the
///         previous bundle hash. <c>RaskHotReloadPhaseTests</c> asserts the assembly carries only
///         this one attribute so a future handler cannot quietly reintroduce the ambiguity.
///     </para>
/// </summary>
internal static class RaskHotReloadHandler
{
    /// <summary>
    ///     Invoked by the hot-reload agent. Never throws: the pipeline reports failures through
    ///     <c>RaskDiagnostics</c> under the <c>Rask.HotReload</c> category and continues, because an
    ///     exception escaping here tears down the watch session.
    /// </summary>
    public static void UpdateApplication(Type[]? updatedTypes)
        => RaskHotReload.RunPhases(AppDomain.CurrentDomain.GetAssemblies(), updatedTypes);
}
