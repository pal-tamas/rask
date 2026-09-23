namespace Rask.Cqrs.Generators.Tests;

public sealed class NotificationSubscriptionGeneratorTests
{
    private const string Preamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Rask.Cqrs;
        namespace Demo;

        public sealed class Order;
        """;

    [Fact]
    public void A_for_attribute_on_a_positional_parameter_records_the_scope_the_key_and_its_policy_check()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record OrderShipped([For<Order>] Guid OrderId, string Status) : INotification;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("CqrsRegistry.ReplaceNotificationScopes(typeof(__RaskCqrsRegistry)", source, StringComparison.Ordinal);
        Assert.Contains("(typeof(global::Demo.OrderShipped), new global::Rask.Cqrs.NotificationScope(typeof(global::Demo.Order)", source, StringComparison.Ordinal);
        Assert.Contains("static n => ((global::Demo.OrderShipped)n).OrderId", source, StringComparison.Ordinal);
        Assert.Contains("CqrsRegistry.CanWatchAsync<global::Demo.Order>(sp, key, ct)", source, StringComparison.Ordinal);
        Assert.Contains("static s => global::System.Guid.Parse(s, global::System.Globalization.CultureInfo.InvariantCulture)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_for_attribute_on_a_property_is_found_too()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed class OrderKnocked : INotification
            {
                [For<Order>]
                public int Number { get; init; }
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("static n => ((global::Demo.OrderKnocked)n).Number", source, StringComparison.Ordinal);
        Assert.Contains("static s => int.Parse(s, global::System.Globalization.CultureInfo.InvariantCulture)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_key_type_crosses_the_wire_its_own_way_and_one_with_no_text_form_stays_in_process()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public enum Colour { Red, Green }
            public readonly record struct Sku(string Value);
            public readonly record struct Ticket(int Value) : IParsable<Ticket>
            {
                public static Ticket Parse(string s, IFormatProvider? provider) => new(int.Parse(s, provider));
                public static bool TryParse(string? s, IFormatProvider? provider, out Ticket result)
                {
                    result = default;
                    return false;
                }
            }
            public sealed record Tagged([For<Order>] string Tag) : INotification;
            public sealed record Painted([For<Order>] Colour Colour) : INotification;
            public sealed record Stocked([For<Order>] Sku Sku) : INotification;
            public sealed record Queued([For<Order>] Ticket Ticket) : INotification;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("((global::Demo.Tagged)n).Tag, static (sp, key, ct) => global::Rask.Cqrs.CqrsRegistry.CanWatchAsync<global::Demo.Order>(sp, key, ct), static s => s)", source, StringComparison.Ordinal);
        Assert.Contains("static s => global::System.Enum.Parse<global::Demo.Colour>(s)", source, StringComparison.Ordinal);
        Assert.Contains("((global::Demo.Stocked)n).Sku, static (sp, key, ct) => global::Rask.Cqrs.CqrsRegistry.CanWatchAsync<global::Demo.Order>(sp, key, ct), null)", source, StringComparison.Ordinal);
        Assert.Contains("static s => global::Demo.Ticket.Parse(s, global::System.Globalization.CultureInfo.InvariantCulture)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_watch_policy_is_registered_like_a_handler_and_kept_under_the_trimmer()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed class WatchingOrders : IWatchPolicy<Order>
            {
                public Task<bool> CanWatchAsync(object key, CancellationToken ct) => Task.FromResult(true);
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var source = run.GeneratedSource("__RaskCqrsRegistry");
        Assert.Contains("TryAdd(services,", source, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Rask.Cqrs.IWatchPolicy<global::Demo.Order>), typeof(global::Demo.WatchingOrders), lifetime", source, StringComparison.Ordinal);
        Assert.Contains("DynamicallyAccessedMemberTypes.PublicConstructors, typeof(global::Demo.WatchingOrders)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assembly_with_no_scoped_notification_installs_no_scope_table()
    {
        var run = CqrsGeneratorFixture.Run(Preamble + """
            public sealed record OrderPlaced(Guid OrderId) : INotification;
            public sealed class OrderPlacedHandler : INotificationHandler<OrderPlaced>
            {
                public Task HandleAsync(OrderPlaced n, CancellationToken ct) => Task.CompletedTask;
            }
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        Assert.DoesNotContain("ReplaceNotificationScopes", run.GeneratedSource("__RaskCqrsRegistry"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_notification_record_s_own_authorization_opens_it_to_remote_subscribers()
    {
        var run = CodecRun("Rask.Cqrs.Client", """
            [Authorize(Roles = "admin")]
            public sealed record OrderPlaced(Guid OrderId) : INotification;
            [AllowAnonymous]
            public sealed record StatusChanged(string Status) : INotification;
            public sealed record Unannounced(Guid Id) : INotification;
            """);

        Assert.Empty(run.GeneratedCompileErrors());
        var codecs = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("SubscribeDeclared = true,\n            SubscribeRoles = \"admin\",", codecs.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("SubscribeDeclared = true,\n            SubscribeAnonymously = true,", codecs.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(codecs, "SubscribeDeclared = true"));
    }

    [Fact]
    public void A_handler_s_authorization_governs_publishing_but_says_nothing_about_subscribing()
    {
        var run = CodecRun("Rask.Cqrs.Client", """
            public sealed record OrderPlaced(Guid OrderId) : INotification;
            [AllowAnonymous]
            public sealed class OrderPlacedHandler : INotificationHandler<OrderPlaced>
            {
                public Task HandleAsync(OrderPlaced n, CancellationToken ct) => Task.CompletedTask;
            }
            """);

        var codecs = run.GeneratedSource("__RaskCqrsCodecs");
        Assert.Contains("AllowAnonymous = true,", codecs, StringComparison.Ordinal);
        Assert.DoesNotContain("SubscribeDeclared", codecs, StringComparison.Ordinal);
    }

    // The attributes are matched by name, so the test declares its own rather than pulling in ASP.NET.
    private static GeneratorRun CodecRun(string transport, string source) =>
        GeneratorHarness.Run(
            Preamble + """

                [AttributeUsage(AttributeTargets.Class)] public sealed class AuthorizeAttribute : Attribute { public string? Roles { get; set; } }
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
