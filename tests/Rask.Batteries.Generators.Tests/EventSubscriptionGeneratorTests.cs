namespace Rask.Batteries.Generators.Tests;

public sealed class EventSubscriptionGeneratorTests
{
    private const string Preamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Rask.Cqrs;
        namespace Demo;

        public sealed record OrderShipped(Guid OrderId, string Status) : IEvent;
        """;

    [Fact]
    public void A_subscription_record_is_registered_with_what_it_carries_how_it_matches_and_its_policy_check()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record WatchOrder(Guid OrderId) : ISubscription<OrderShipped>
            {
                public bool Matches(OrderShipped e) => e.OrderId == OrderId;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("CqrsRegistry.ReplaceSubscriptions(typeof(__RaskCqrsRegistry)", source, StringComparison.Ordinal);
        Assert.Contains("(typeof(global::Demo.WatchOrder), new global::Rask.Cqrs.SubscriptionRegistration(", source, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.OrderShipped),", source, StringComparison.Ordinal);
        Assert.Contains(
            "static (s, n) => ((global::Rask.Cqrs.ISubscription<global::Demo.OrderShipped>)s).Matches((global::Demo.OrderShipped)n),",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "CqrsRegistry.CanWatch<global::Demo.WatchOrder>(sp, (global::Demo.WatchOrder)s, ct)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_subscription_written_as_a_struct_or_with_an_explicit_implementation_is_found_too()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public readonly record struct WatchAll() : ISubscription<OrderShipped>
            {
                public bool Matches(OrderShipped e) => true;
            }

            public sealed record WatchExplicit(Guid OrderId) : ISubscription<OrderShipped>
            {
                bool ISubscription<OrderShipped>.Matches(OrderShipped e) => e.OrderId == OrderId;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("typeof(global::Demo.WatchAll)", source, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.WatchExplicit)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_watch_policy_is_registered_like_a_handler_and_kept_under_the_trimmer()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record WatchOrder(Guid OrderId) : ISubscription<OrderShipped>
            {
                public bool Matches(OrderShipped e) => e.OrderId == OrderId;
            }

            public sealed class WatchingOrders : IWatchPolicy<WatchOrder>
            {
                public Task<bool> CanWatch(WatchOrder s, CancellationToken ct) => Task.FromResult(true);
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("TryAdd(services,", source, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Rask.Cqrs.IWatchPolicy<global::Demo.WatchOrder>), typeof(global::Demo.WatchingOrders), lifetime", source, StringComparison.Ordinal);
        Assert.Contains("DynamicallyAccessedMemberTypes.PublicConstructors, typeof(global::Demo.WatchingOrders)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_that_declares_no_subscription_installs_no_subscription_table()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed class OrderShippedHandler : IEventHandler<OrderShipped>
            {
                public Task Handle(OrderShipped n) => Task.CompletedTask;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("ReplaceSubscriptions", run.GeneratedSource("__RaskCqrsRegistry"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_subscription_record_crosses_the_wire_as_its_own_contract_carrying_the_event()
    {
        var run = CodecRun("Rask.Cqrs.Client", """
            public sealed record WatchOrder(Guid OrderId) : ISubscription<OrderShipped>
            {
                public bool Matches(OrderShipped e) => e.OrderId == OrderId;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var codecs = run.GeneratedSource("__RaskCqrsCodecs").ReplaceLineEndings("\n");
        Assert.Contains("MessageType = typeof(global::Demo.WatchOrder),", codecs, StringComparison.Ordinal);
        Assert.Contains("Kind = global::Rask.Cqrs.RemoteMessageKind.Subscription,", codecs, StringComparison.Ordinal);
        Assert.Contains("ResultType = typeof(global::Demo.OrderShipped),", codecs, StringComparison.Ordinal);

        // It is opened, never sent: no request invoker, so nothing can dispatch it as a message.
        var entry = codecs[codecs.IndexOf("MessageType = typeof(global::Demo.WatchOrder),", StringComparison.Ordinal)..];
        Assert.DoesNotContain("Invoker =", entry[..entry.IndexOf("};", StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public void A_event_record_s_own_authorization_opens_it_to_remote_subscribers()
    {
        var run = CodecRun("Rask.Cqrs.Client", """
            [Authorize(Roles = "admin")]
            public sealed record OrderPlaced(Guid OrderId) : IEvent;
            [AllowAnonymous]
            public sealed record StatusChanged(string Status) : IEvent;
            public sealed record Unannounced(Guid Id) : IEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var codecs = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("SubscribeDeclared = true,\n            SubscribeRoleSets = [\"admin\"],", codecs.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("SubscribeDeclared = true,\n            SubscribeAnonymously = true,", codecs.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(codecs, "SubscribeDeclared = true"));
    }

    [Fact]
    public void A_handler_s_authorization_governs_publishing_but_says_nothing_about_subscribing()
    {
        var run = CodecRun("Rask.Cqrs.Client", """
            public sealed record OrderPlaced(Guid OrderId) : IEvent;
            [AllowAnonymous]
            public sealed class OrderPlacedHandler : IEventHandler<OrderPlaced>
            {
                public Task Handle(OrderPlaced n) => Task.CompletedTask;
            }
            """);

        var codecs = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("AllowAnonymous = true,", codecs, StringComparison.Ordinal);
        Assert.DoesNotContain("SubscribeDeclared", codecs, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_Authorize_on_a_handler_and_on_a_record_reaches_the_manifest()
    {
        // #1178: one policy and one roles string were kept per type, so the last attribute won.
        var run = CodecRun("Rask.Cqrs.Client", """
            public sealed record Charge(Guid OrderId) : ICommand;
            [Authorize(Policy = "members")]
            [Authorize(Policy = "billing", Roles = "admin,owner")]
            [Authorize(Roles = "finance")]
            public sealed class ChargeHandler : ICommandHandler<Charge>
            {
                public Task Handle(Charge c) => Task.CompletedTask;
            }
            [Authorize(Policy = "members")]
            [Authorize(Policy = "audit")]
            public sealed record Charged(Guid OrderId) : IEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var codecs = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("Policies = [\"members\", \"billing\"],", codecs, StringComparison.Ordinal);
        Assert.Contains("RoleSets = [\"admin,owner\", \"finance\"],", codecs, StringComparison.Ordinal);
        Assert.Contains("SubscribePolicies = [\"members\", \"audit\"],", codecs, StringComparison.Ordinal);
    }

    [Fact]
    public void A_policy_name_holding_a_quote_is_written_as_a_literal()
    {
        var run = CodecRun("Rask.Cqrs.Client", """
            [Authorize(Policy = "say \"when\"")]
            public sealed record Poured(Guid Id) : IEvent;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.Contains(
            """SubscribePolicies = ["say \"when\""],""", run.GeneratedSource("__RaskCqrsCodecs"), StringComparison.Ordinal);
    }

    // The attributes are matched by name, so the test declares its own rather than pulling in ASP.NET.
    private static GeneratorRun CodecRun(string transport, string source) =>
        GeneratorHarness.Run(
            Preamble + """

                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public sealed class AuthorizeAttribute : Attribute { public string? Policy { get; set; } public string? Roles { get; set; } }
                [AttributeUsage(AttributeTargets.Class)] public sealed class AllowAnonymousAttribute : Attribute;

                """ + source,
            new CqrsCodecGenerator(),
            "Rask.Cqrs", transport, "Rask.Wire", "Microsoft.Extensions.DependencyInjection.Abstractions");

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
