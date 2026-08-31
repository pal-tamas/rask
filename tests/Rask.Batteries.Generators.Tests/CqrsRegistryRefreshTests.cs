namespace Rask.Cqrs.Generators.Tests;

/// <summary>
///     The dispatch table is populated by a <c>[ModuleInitializer]</c>, which the runtime never re-runs
///     after a hot-reload apply — so editing a handler under <c>dotnet watch</c> left the old invoker in
///     place. The generator now emits a re-invocable <c>RefreshAll()</c> that Rask's hot-reload
///     coordinator calls by name.
///     <para>
///         Unlike the other registries, this one is a deliberate <em>partial</em> refresh. See
///         <see cref="RefreshAll_omits_service_registrations_so_hot_reload_cannot_leak_them" />.
///     </para>
/// </summary>
public class CqrsRegistryRefreshTests
{
    // Must match the entry in RaskHotReload.RefreshTargetTypeNames — the coordinator resolves it with
    // Assembly.GetType(name), and this class is emitted into the global namespace.
    private const string RefreshTargetTypeName = "__RaskCqrsRegistry";

    private const string OneCommandOneNotification = """
        using System.Threading;
        using System.Threading.Tasks;
        using Rask.Cqrs;
        namespace Demo;

        public sealed record Ping : ICommand;
        public sealed class PingHandler : ICommandHandler<Ping>
        {
            public Task HandleAsync(Ping command, CancellationToken ct) => Task.CompletedTask;
        }

        public sealed record Pinged : INotification;
        public sealed class PingedHandler : INotificationHandler<Pinged>
        {
            public Task HandleAsync(Pinged notification, CancellationToken ct) => Task.CompletedTask;
        }
        """;

    [Fact]
    public void Init_calls_RefreshAll_and_RefreshAll_holds_the_dispatch_table()
    {
        var run = CqrsGeneratorFixture.Run(OneCommandOneNotification);
        var source = run.GeneratedSource(RefreshTargetTypeName);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            "[global::System.Runtime.CompilerServices.ModuleInitializer]", source, StringComparison.Ordinal);
        Assert.Contains("internal static void RefreshAll()", source, StringComparison.Ordinal);

        var refresh = Body(source, "internal static void RefreshAll()");
        Assert.Contains("(typeof(global::Demo.Ping), __Request_", refresh, StringComparison.Ordinal);
        Assert.Contains("(typeof(global::Demo.Pinged), __Notify_", refresh, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_replaces_both_dispatch_tables_rather_than_upserting_into_them()
    {
        // Sibling of #537 in the two serializer registries, and the same shape of bug: upserts only ever
        // add or overwrite, so deleting the last handler for a request left its invoker in the table and
        // dispatch kept succeeding through IL that no longer had a handler behind it. Replacing this
        // assembly's whole contribution makes a deletion take effect under `rask dev`.
        var source = CqrsGeneratorFixture.Run(OneCommandOneNotification).GeneratedSource(RefreshTargetTypeName);

        Assert.Contains(
            "global::Rask.Cqrs.CqrsRegistry.ReplaceRequests(typeof(__RaskCqrsRegistry), ",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Rask.Cqrs.CqrsRegistry.ReplaceNotifications(typeof(__RaskCqrsRegistry), ",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CqrsRegistry.RegisterRequest(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CqrsRegistry.RegisterNotification(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_omits_service_registrations_so_hot_reload_cannot_leak_them()
    {
        // CqrsRegistry.RegisterServices enqueues onto a ConcurrentQueue that is never drained —
        // ApplyRegistrations only iterates it. Re-running those on every save would grow the queue
        // without bound for the life of the watch session, so they stay in Init() (which runs once)
        // while only the idempotent dictionary upserts move into RefreshAll().
        var source = CqrsGeneratorFixture.Run(OneCommandOneNotification).GeneratedSource(RefreshTargetTypeName);

        var refresh = Body(source, "internal static void RefreshAll()");
        Assert.DoesNotContain("RegisterServices", refresh, StringComparison.Ordinal);

        var init = Body(source, "internal static void Init()");
        Assert.Contains("RefreshAll();", init, StringComparison.Ordinal);
        Assert.Contains("RegisterServices", init, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAll_is_internal_static_so_the_coordinator_can_reflect_onto_it()
    {
        // Looked up with BindingFlags.Static | NonPublic | Public; a private or instance member
        // would be found by nothing and fail silently.
        Assert.Contains(
            "internal static void RefreshAll()",
            CqrsGeneratorFixture.Run(OneCommandOneNotification).GeneratedSource(RefreshTargetTypeName),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_emitted_class_matches_the_name_the_coordinator_looks_for()
    {
        Assert.Contains(
            $"internal static class {RefreshTargetTypeName}",
            CqrsGeneratorFixture.Run(OneCommandOneNotification).GeneratedSource(RefreshTargetTypeName),
            StringComparison.Ordinal);
    }

    // Returns the text of the method body that starts at `signature`, up to its closing brace at the
    // same indent. Good enough for generated code, which is emitted at a fixed indent.
    private static string Body(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' not found in generated source:\n{source}");

        var open = source.IndexOf("\n    {", start, StringComparison.Ordinal);
        var close = source.IndexOf("\n    }", open, StringComparison.Ordinal);
        return source[open..close];
    }
}
