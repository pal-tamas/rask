using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.MySql.Tests;

/// <summary>
/// Oracle's provider loses a <see cref="DateTimeOffset"/>'s fractional seconds; the convention <c>UseRaskMySql</c>
/// registers stores one as its UTC <see cref="DateTime"/> instead. The mapping is read offline from the model; that
/// the digits then survive a real server is proven in <c>Rask.Providers.E2E.Tests</c>.
/// </summary>
public sealed class DateTimeOffsetAsUtcConventionTests
{
    private const string ConnectionString = "Server=localhost;Database=rask;User ID=rask;Password=rask";

    [Fact]
    public void An_unconfigured_date_time_offset_is_stored_as_a_microsecond_utc_date_time()
    {
        var property = Property(nameof(Stamp.At));

        Assert.Same(DateTimeOffsetAsUtcConvention.Converter, property.GetValueConverter());
        Assert.Equal("datetime(6)", property.GetRelationalTypeMapping().StoreType);
    }

    [Fact]
    public void A_nullable_date_time_offset_is_converted_too()
    {
        var property = Property(nameof(Stamp.Seen));

        Assert.Same(DateTimeOffsetAsUtcConvention.Converter, property.GetValueConverter());
        Assert.Equal("datetime(6)", property.GetRelationalTypeMapping().StoreType);
    }

    [Fact]
    public void A_date_time_offset_inside_a_complex_type_is_converted_too()
    {
        using var db = NewContext();
        var start = db.Model.FindEntityType(typeof(Stamp))!.FindComplexProperty(nameof(Stamp.Window))!
            .ComplexType.FindProperty(nameof(Window.Start))!;

        Assert.Same(DateTimeOffsetAsUtcConvention.Converter, start.GetValueConverter());
    }

    [Fact]
    public void An_explicit_precision_is_kept_and_still_converted()
    {
        // The reader truncates whatever the column's precision, so a configured one needs the conversion as much.
        var property = Property(nameof(Stamp.Millis));

        Assert.Same(DateTimeOffsetAsUtcConvention.Converter, property.GetValueConverter());
        Assert.Equal("datetime(3)", property.GetRelationalTypeMapping().StoreType);
    }

    [Fact]
    public void An_explicit_column_type_is_kept()
    {
        Assert.Equal("datetime", Property(nameof(Stamp.Seconds)).GetRelationalTypeMapping().StoreType);
    }

    [Fact]
    public void A_property_with_its_own_converter_is_left_alone()
    {
        var property = Property(nameof(Stamp.Ticks));

        Assert.IsType<DateTimeOffsetToBinaryConverter>(property.GetValueConverter());
        Assert.Equal("bigint", property.GetRelationalTypeMapping().StoreType);
    }

    [Fact]
    public void A_date_time_is_untouched()
    {
        var property = Property(nameof(Stamp.When));

        Assert.Null(property.GetValueConverter());
        Assert.Equal("datetime(6)", property.GetRelationalTypeMapping().StoreType);
    }

    [Fact]
    public void The_converter_keeps_the_instant_and_its_microseconds()
    {
        var written = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)).AddTicks(1_234_560);

        var stored = (DateTime)DateTimeOffsetAsUtcConvention.Converter.ConvertToProvider(written)!;
        var read = (DateTimeOffset)DateTimeOffsetAsUtcConvention.Converter.ConvertFromProvider(stored)!;

        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(written.UtcTicks, stored.Ticks);
        Assert.Equal(written, read);
        Assert.Equal(TimeSpan.Zero, read.Offset);
    }

    [Fact]
    public void Without_UseRaskMySql_the_provider_maps_whole_seconds()
    {
        // Pins half of the defect the convention exists for (the other half, the reader, needs a server). If a
        // provider release changes it, this fails and the convention is due a second look.
        var options = new DbContextOptionsBuilder<StampContext>().UseMySQL(ConnectionString).Options;
        using var db = new StampContext(options);
        var property = db.Model.FindEntityType(typeof(Stamp))!.FindProperty(nameof(Stamp.At))!;

        Assert.Null(property.GetValueConverter());
        Assert.Equal("datetime", property.GetRelationalTypeMapping().StoreType);
    }

    private static IProperty Property(string name)
    {
        using var db = NewContext();
        return db.Model.FindEntityType(typeof(Stamp))!.FindProperty(name)!;
    }

    private static StampContext NewContext() =>
        new(new DbContextOptionsBuilder<StampContext>().UseRaskMySql(UseRaskMySqlTests.ServicesWith(new() { ["Rask:ConnectionStrings:App"] = ConnectionString })).Options);

    private sealed class Stamp
    {
        public Guid Id { get; set; }

        public DateTimeOffset At { get; set; }

        public DateTimeOffset? Seen { get; set; }

        public DateTimeOffset Millis { get; set; }

        public DateTimeOffset Seconds { get; set; }

        public DateTimeOffset Ticks { get; set; }

        public DateTime When { get; set; }

        public Window Window { get; set; } = new();
    }

    private sealed class Window
    {
        public DateTimeOffset Start { get; set; }
    }

    private sealed class StampContext(DbContextOptions<StampContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var stamp = modelBuilder.Entity<Stamp>();
            stamp.Property(s => s.Millis).HasPrecision(3);
            stamp.Property(s => s.Seconds).HasColumnType("datetime");
            stamp.Property(s => s.Ticks).HasConversion(new DateTimeOffsetToBinaryConverter());
            stamp.ComplexProperty(s => s.Window);
        }
    }
}
