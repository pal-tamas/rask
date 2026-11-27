using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Rask.Data;
using Rask.Values;

// The fixtures sit outside the Rask namespaces on purpose: Rask's own types are the framework's and are left alone.
namespace Shop.Deliveries
{
    /// <summary>A delivery's moment, as an app declares one.</summary>
    public sealed partial record DeliveredAt(DateTime Value) : Moment(Value);

    /// <summary>An entity of the domain that holds its time as Rask asks: a moment.</summary>
    public sealed class Delivery : IEntity
    {
        public int Id { get; set; }

        public DeliveredAt At { get; set; } = null!;
    }

    /// <summary>An entity of the domain that still holds a bare DateTime.</summary>
    public sealed class Shipment : IEntity
    {
        public int Id { get; set; }

#pragma warning disable RASK147 // the bare DateTime the model build refuses on PostgreSQL is what is under test
        public DateTime ShippedAt { get; set; }
#pragma warning restore RASK147
    }

    /// <summary>A table that is no entity of the domain: a row an import stages.</summary>
    public sealed class StagedRow
    {
        public int Id { get; set; }

        public DateTime ReadAt { get; set; }
    }

    public sealed class DeliveryContext(DbContextOptions<DeliveryContext> options) : RaskDbContext(options)
    {
        public DbSet<Delivery> Deliveries => Set<Delivery>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            ValueObjectColumn.Map<DeliveredAt, DateTime>(modelBuilder.Entity<Delivery>().Property(d => d.At), v => v.Value);
            modelBuilder.Entity<StagedRow>();
        }
    }

    public sealed class ShipmentContext(DbContextOptions<ShipmentContext> options) : RaskDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Shipment>();
        }
    }

    /// <summary>The same entity, its DateTime stored the way the app said: through a converter of its own.</summary>
    public sealed class ConvertedShipmentContext(DbContextOptions<ConvertedShipmentContext> options) : RaskDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Shipment>().Property(s => s.ShippedAt).HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        }
    }
}

namespace Rask.Postgres.Tests
{
    using Shop.Deliveries;

    /// <summary>
    ///     On PostgreSQL a moment's day is read in the app's zone, and a bare <see cref="DateTime" /> on an entity of the
    ///     domain is refused as the model is built — Npgsql would refuse a wall-clock one only as the row is saved.
    /// </summary>
    public sealed class MomentColumnTests
    {
        private static readonly IServiceProvider Services = new ConfigurationServices(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Rask:ConnectionStrings:App"] = "Host=unused;Database=unused" }).Build());

        private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

        private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        [Fact]
        public void A_moment_s_day_is_read_on_the_app_s_wall_calendar()
        {
            using var zone = AmbientClock.UseZone(Budapest);
            using var context = new DeliveryContext(On<DeliveryContext>());

            var sql = context.Deliveries.Where(d => d.At.Day == new DateOnly(2026, 10, 25)).ToQueryString();

            Assert.Contains("timezone('Europe/Budapest', d.\"At\")::date", sql, StringComparison.Ordinal);
        }

        [Fact]
        public void The_same_query_run_again_under_another_zone_reads_that_zone_s_day()
        {
            using var context = new DeliveryContext(On<DeliveryContext>());
            var day = new DateOnly(2026, 10, 25);

            var inBudapest = InZone(Budapest, () => context.Deliveries.Where(d => d.At.Day == day).ToQueryString());
            var inNewYork = InZone(NewYork, () => context.Deliveries.Where(d => d.At.Day == day).ToQueryString());

            Assert.Contains("timezone('Europe/Budapest', d.\"At\")::date", inBudapest, StringComparison.Ordinal);
            Assert.Contains("timezone('America/New_York', d.\"At\")::date", inNewYork, StringComparison.Ordinal);
        }

        [Fact]
        public void A_bare_date_and_time_on_an_entity_of_the_domain_is_refused_with_the_way_to_hold_it()
        {
            using var context = new ShipmentContext(On<ShipmentContext>());

            var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

            Assert.Contains("Shipment.ShippedAt is a bare DateTime", refused.Message, StringComparison.Ordinal);
            Assert.Contains("Moment", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_date_and_time_the_app_converted_itself_and_a_table_outside_the_domain_are_left_alone()
        {
            using var converted = new ConvertedShipmentContext(On<ConvertedShipmentContext>());
            using var staged = new DeliveryContext(On<DeliveryContext>());

            var shipment = converted.Model.FindEntityType(typeof(Shipment));
            var row = staged.Model.FindEntityType(typeof(StagedRow));

            Assert.NotNull(shipment);
            Assert.NotNull(row);
        }

        private static string InZone(TimeZoneInfo zone, Func<string> query)
        {
            using var ambient = AmbientClock.UseZone(zone);
            return query();
        }

        private static DbContextOptions<TContext> On<TContext>()
            where TContext : DbContext =>
            new DbContextOptionsBuilder<TContext>().UseRaskPostgres(Services).Options;

        // Just the configuration: that is all UseRaskPostgres asks the provider for.
        private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
        {
            public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
        }
    }
}
