// Stand-ins for the generator-emitted registry classes, one per entry in
// RaskHotReload.RefreshTargetTypeNames.
//
// These MUST be top-level, in exactly the namespace the coordinator looks for, because it resolves
// them with Assembly.GetType(name) — which does not match nested types. The previous generation of
// these tests used classes nested inside the test class, so asm.GetType("__RaskScopedCssRegistration")
// always returned null and the reflective invoke loop was never actually exercised; only the old
// name-based gate was. Four of the six live in the global namespace, mirroring the real generated
// output.
//
// Each RefreshAll() is inert until a test arms its Hook. That matters because the coordinator scans
// the whole AppDomain: unrelated tests that trigger a hot-reload apply would otherwise mutate the
// shared ScopedAssetRegistry out from under the [Collection("ScopedAssets")] suite.

internal static class __RaskScopedCssRegistration
{
    internal static Action? Hook;
    internal static void RefreshAll() => Hook?.Invoke();
}

internal static class __RaskScopedJsRegistration
{
    internal static Action? Hook;
    internal static void RefreshAll() => Hook?.Invoke();
}

// NOTE: there is deliberately no __RaskRoutesRegistry sentinel. This test assembly has [Route]
// pages, so RoutesGenerator already emits the real one into the global namespace and a stand-in
// would collide with it (CS0101). Route refresh is covered end-to-end by
// RouteRegistryHotReloadTests plus the RoutesGenerator suite.

internal static class __RaskCqrsRegistry
{
    internal static Action? Hook;
    internal static void RefreshAll() => Hook?.Invoke();
}

namespace Rask.Jobs.Generated
{
    internal static class __RaskJobsRegistry
    {
        internal static Action? Hook;
        internal static void RefreshAll() => Hook?.Invoke();
    }
}

namespace Rask.Outbox.Generated
{
    internal static class __RaskOutboxRegistry
    {
        internal static Action? Hook;
        internal static void RefreshAll() => Hook?.Invoke();
    }
}

namespace Rask.Core.Tests.HotReload
{
    /// <summary>
    ///     Arms and disarms every sentinel as a set, so a test cannot leak one into the next.
    /// </summary>
    internal static class RefreshTargets
    {
        internal static IDisposable Arm(
            Action? css = null, Action? js = null,
            Action? cqrs = null, Action? jobs = null, Action? outbox = null)
        {
            global::__RaskScopedCssRegistration.Hook = css;
            global::__RaskScopedJsRegistration.Hook = js;
            global::__RaskCqrsRegistry.Hook = cqrs;
            Jobs.Generated.__RaskJobsRegistry.Hook = jobs;
            Outbox.Generated.__RaskOutboxRegistry.Hook = outbox;
            return new Disarm();
        }

        private sealed class Disarm : IDisposable
        {
            public void Dispose()
            {
                global::__RaskScopedCssRegistration.Hook = null;
                global::__RaskScopedJsRegistration.Hook = null;
                global::__RaskCqrsRegistry.Hook = null;
                Jobs.Generated.__RaskJobsRegistry.Hook = null;
                Outbox.Generated.__RaskOutboxRegistry.Hook = null;
            }
        }
    }
}
