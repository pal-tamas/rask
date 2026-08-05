using System.Reflection;
using Rask.Example.Shop.Features.Shared;

namespace Rask.Example.Shop.Tests;

/// <summary>
///     Binds Rask's hot-reload coordinator to what the generators actually emit.
///     <para>
///         The coordinator lives in <c>Rask.Core</c> and reaches the other packages' registries by
///         name — <c>Rask.Cqrs</c>, <c>Rask.Jobs</c> and <c>Rask.Outbox</c> deliberately do not
///         reference <c>Rask.Core</c>. Nothing in the compiler checks those strings, so a renamed
///         generated class would leave that registry silently frozen at its startup state under
///         <c>dotnet watch</c>: no compile error, no test failure, just a registry that never picks up
///         an edit.
///     </para>
///     <para>
///         The Shop sample is the only app in the repo that turns on every battery at once, so it is
///         the one place all four generated registries exist in a single assembly. Keep this list in
///         lockstep with <c>RaskHotReload.RefreshTargetTypeNames</c> (internal to Rask.Core, which is
///         why it is repeated rather than referenced); <c>HotReloadPhaseTests</c> pins the array
///         itself against the same literals.
///     </para>
/// </summary>
public class HotReloadRefreshTargetTests
{
    private static readonly Assembly _app = typeof(AppDbContext).Assembly;

    [Theory]
    [InlineData("__RaskRoutesRegistry")]
    [InlineData("__RaskCqrsRegistry")]
    [InlineData("Rask.Jobs.Generated.__RaskJobsRegistry")]
    [InlineData("Rask.Outbox.Generated.__RaskOutboxRegistry")]
    // The two scoped-asset registrations are deliberately absent: they are only emitted for a project
    // that actually has a .css/.js sibling, and the Shop sample has none. Their refresh path is
    // covered by the Core suite instead.
    public void The_coordinator_can_resolve_and_invoke_every_refresh_target(string typeName)
    {
        // Assembly.GetType(name) is exactly how the coordinator resolves these — note it does NOT
        // match nested types, so the emitted class must stay top-level in this namespace.
        var type = _app.GetType(typeName, throwOnError: false);
        Assert.True(type is not null, $"'{typeName}' is not in {_app.GetName().Name}. " +
                                      "A generator renamed or stopped emitting it, so that registry no longer hot-reloads.");

        // Same BindingFlags the coordinator uses. The generated members are internal.
        var refreshAll = type!.GetMethod(
            "RefreshAll", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.True(refreshAll is not null, $"'{typeName}' has no static RefreshAll() — the coordinator would skip it.");
        Assert.Empty(refreshAll!.GetParameters());
    }

    [Fact]
    public void Refreshing_a_registry_twice_is_idempotent()
    {
        // The coordinator re-invokes RefreshAll() on every apply, including applies that touched
        // nothing relevant. Registering the same jobs/events again must not throw or accumulate.
        var jobs = _app.GetType("Rask.Jobs.Generated.__RaskJobsRegistry", throwOnError: true)!;
        var refreshAll = jobs.GetMethod(
            "RefreshAll", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        refreshAll.Invoke(null, null);
        refreshAll.Invoke(null, null);

        // Still exactly one usable registration afterwards — the job round-trips.
        var (typeName, payload) = Rask.Jobs.JobSerializerRegistry.Serialize(new Features.Orders.PurgeStaleCarts());
        Assert.NotNull(Rask.Jobs.JobSerializerRegistry.Deserialize(typeName, payload));
    }
}
