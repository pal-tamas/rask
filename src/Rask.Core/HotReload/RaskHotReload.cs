using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Core.ScopedAssets;

namespace Rask.Core.HotReload;

/// <summary>
///     The ordered pipeline behind Rask's single <c>[MetadataUpdateHandler]</c>. Runs after
///     <c>dotnet watch</c> applies new IL, refreshing everything the generators registered through
///     a <c>[ModuleInitializer]</c> — which the runtime never re-runs — and then repainting every
///     live session.
///     <para>
///         Order is the whole point. This used to be three independent handlers (scoped CSS,
///         scoped JS, session re-render) whose relative order the runtime does not define; when the
///         re-render happened to run first, the frame carried the previous bundle hash and a CSS
///         edit only appeared on the next interaction. Assets and registries are now refreshed to
///         completion before any session re-renders.
///     </para>
/// </summary>
internal static class RaskHotReload
{
    private const string RefreshMethodName = "RefreshAll";

    /// <summary>
    ///     Every generator-emitted registry that exposes a re-invocable <c>RefreshAll()</c>, in
    ///     refresh order. All of these are emitted into the *user's* assembly, so the coordinator
    ///     reaches them by name — <c>Rask.Cqrs</c>, <c>Rask.Jobs</c> and <c>Rask.Outbox</c> do not
    ///     reference <c>Rask.Core</c> and must not start to.
    ///     <para>
    ///         Adding a generator that registers through a <c>[ModuleInitializer]</c> without adding
    ///         it here means it silently will not hot-reload. Each generator's test suite asserts
    ///         its emitted class name appears in this array.
    ///     </para>
    /// </summary>
    internal static readonly string[] RefreshTargetTypeNames =
    [
        "__RaskRoutesRegistry",
        "__RaskCqrsRegistry",
        "__RaskValidatorRegistry",
        "Rask.Jobs.Generated.__RaskJobsRegistry",
        "Rask.Outbox.Generated.__RaskOutboxRegistry"
    ];

    internal const string ScopedCssRegistrationTypeName = "__RaskScopedCssRegistration";
    internal const string ScopedJsRegistrationTypeName = "__RaskScopedJsRegistration";

    /// <summary>
    ///     Raised once per apply, after every tracked session has finished re-rendering. The Server
    ///     host subscribes in Development to push a "hot reload applied" frame to the browser.
    /// </summary>
    internal static event Action? Applied;

    internal static void RaiseApplied() => Applied?.Invoke();

    /// <summary>
    ///     Runs the full pipeline over <paramref name="assemblies" />. Takes the assembly list as a
    ///     parameter rather than reading <c>AppDomain.CurrentDomain</c> directly so tests can drive
    ///     every phase against a controlled set.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan for generated registry classes.</param>
    /// <param name="updatedTypes">
    ///     The types the runtime reports as changed. Deliberately ignored: an edit to a helper or a
    ///     static that a component calls does not appear there, so gating on it would miss real
    ///     changes. Refreshing unconditionally is cheap — an apply that touched no CSS stages an
    ///     identical set, which the registry detects and discards without moving its version.
    /// </param>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Hot reload only runs under dotnet watch; trimmed publishes never invoke it.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Same as IL2026.")]
    internal static void RunPhases(IReadOnlyList<Assembly> assemblies, Type[]? updatedTypes)
    {
        _ = updatedTypes;

        // Phase 1 + 2 — scoped assets, staged so concurrent renders never observe a partial set.
        RefreshScopedAssets(
            Collect(assemblies, ScopedCssRegistrationTypeName),
            ScopedAssetRegistry.BeginCssRefresh,
            ScopedAssetRegistry.EndCssRefresh);

        RefreshScopedAssets(
            Collect(assemblies, ScopedJsRegistrationTypeName),
            ScopedAssetRegistry.BeginJsRefresh,
            ScopedAssetRegistry.EndJsRefresh);

        // Phase 3 — routes and the satellite registries. Each RefreshAll() is idempotent and
        // replace-semantics by construction, so there is no clear step and therefore no window in
        // which a route or a dispatch handler is missing.
        foreach (var typeName in RefreshTargetTypeNames)
        {
            InvokeAll(Collect(assemblies, typeName));
        }

        // Phase 4 — repaint. Off the caller's thread because the runtime invokes update handlers
        // on the hot-reload agent's thread and a render can await application code.
        _ = Task.Run(async () =>
        {
            bool repainted;
            try
            {
                repainted = await LiveSessionBase.RerenderAllForHotReloadAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Defence in depth: RerenderAllForHotReloadAsync catches per session and does not throw,
                // so this is the "it started to" case rather than a session fault.
                RaskDiagnostics.Report(
                    RaskLogLevel.Warning, "Rask.HotReload", "Rask: hot-reload rerender failed", ex);
                repainted = false;
            }

            // Applied drives the browser's green "Hot reload applied" pill, and the repaint is the whole
            // of what the developer can see. Announcing it for an edit that never reached the page told
            // them the opposite of the truth, with the only evidence on the server's stderr (#603). A
            // missing pill is the honest signal: nothing visibly changed, because nothing did.
            if (repainted)
            {
                RaiseApplied();
            }
        });
    }

    private static void RefreshScopedAssets(
        List<MethodInfo> methods, Action begin, Func<bool> end)
    {
        // No generated registration in any loaded assembly means this app has no scoped assets of
        // this kind. Opening a staging window would swap an empty map over the live one on End.
        if (methods.Count == 0)
        {
            return;
        }

        begin();
        try
        {
            InvokeAll(methods);
        }
        finally
        {
            // Must run even if every invoke faulted: while staging is open the registry diverts
            // all registrations, so an abandoned window would swallow every later one.
            end();
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Hot reload only runs under dotnet watch; trimmed publishes never invoke it.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Same as IL2026.")]
    private static List<MethodInfo> Collect(IReadOnlyList<Assembly> assemblies, string typeName)
    {
        var found = new List<MethodInfo>();
        foreach (var asm in assemblies)
        {
            try
            {
                var type = asm.GetType(typeName, false);
                var method = type?.GetMethod(
                    RefreshMethodName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (method is not null)
                {
                    found.Add(method);
                }
            }
            catch
            {
                // A single unloadable/reflection-hostile assembly must not stop the rest.
            }
        }

        return found;
    }

    private static void InvokeAll(List<MethodInfo> methods)
    {
        foreach (var method in methods)
        {
            try
            {
                method.Invoke(null, null);
            }
            catch (Exception ex)
            {
                RaskDiagnostics.Report(
                    RaskLogLevel.Warning, "Rask.HotReload",
                    $"Rask: hot-reload refresh failed for {method.DeclaringType?.FullName}", ex);
            }
        }
    }
}
