using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests.NamingShapes;

/// <summary>Rask's own names, with nothing stated: tables plural in snake case, columns in snake case, constraints prefixed.</summary>
public sealed class DefaultNamingTests
{
    private static readonly IModel Named = Model();

    [Fact]
    public void A_table_is_its_class_in_snake_case_and_plural()
    {
        var types = new[] { typeof(Fueling), typeof(TankLevel), typeof(Person), typeof(VehicleCategory) };

        var tables = types.Select(t => Named.FindEntityType(t)!.GetTableName());

        Assert.Equal(["fuelings", "tank_levels", "people", "vehicle_categories"], tables);
    }

    [Fact]
    public void A_column_is_its_property_in_snake_case()
    {
        var fueling = Named.FindEntityType(typeof(Fueling))!;

        var columns = new[] { nameof(Fueling.Id), nameof(Fueling.PistolId), nameof(Fueling.CreatedAt) }.Select(p => Column(fueling, p));

        Assert.Equal(["id", "pistol_id", "created_at"], columns);
    }

    [Fact]
    public void A_value_objects_column_is_its_path_in_snake_case()
    {
        var km = Named.FindEntityType(typeof(Fueling))!.FindComplexProperty(nameof(Fueling.Km))!.ComplexType;

        var columns = new[] { nameof(Odometer.Start), nameof(Odometer.End) }
            .Select(p => km.FindProperty(p)!.GetColumnName(StoreObjectIdentifier.Table("fuelings")));

        Assert.Equal(["km_start", "km_end"], columns);
    }

    [Fact]
    public void Every_constraint_is_its_kinds_prefix_its_table_and_its_columns()
    {
        var fueling = Named.FindEntityType(typeof(Fueling))!;
        var pistol = Named.FindEntityType(typeof(Pistol))!;

        var names = new[]
        {
            fueling.FindPrimaryKey()!.GetName(),
            fueling.GetForeignKeys().Single().GetConstraintName(),
            fueling.GetIndexes().Single(i => !i.IsUnique && i.Properties[0].Name == nameof(Fueling.CreatedAt)).GetDatabaseName(),
            fueling.GetIndexes().Single(i => i.IsUnique).GetDatabaseName(),
            pistol.GetKeys().Single(k => !k.IsPrimaryKey()).GetName(),
        };

        Assert.Equal(
            ["pk_fuelings", "fk_fuelings_pistol_id", "ix_fuelings_created_at", "uq_fuelings_pistol_id_created_at", "uq_pistols_number"],
            names);
    }

    [Fact]
    public void A_composite_key_names_both_columns()
    {
        var level = Named.FindEntityType(typeof(TankLevel))!;

        var key = (level.FindPrimaryKey()!.GetName(), level.FindPrimaryKey()!.Properties.Select(p => Column(level, p.Name)));

        Assert.Equal("pk_tank_levels", key.Item1);
        Assert.Equal(["tank_id", "counted_at"], key.Item2);
    }

    [Fact]
    public void A_shadow_foreign_key_is_named_like_any_column_and_so_is_its_constraint()
    {
        var reading = Named.FindEntityType(typeof(Reading))!;

        var key = reading.GetForeignKeys().Single();

        Assert.Equal("pistol_id", Column(reading, key.Properties[0].Name));
        Assert.Equal("fk_readings_pistol_id", key.GetConstraintName());
    }

    [Fact]
    public void An_owned_type_in_its_owners_table_takes_its_navigations_prefix_and_one_of_its_own_a_plural_table()
    {
        var address = Named.FindEntityType(typeof(Address))!;
        var leg = Named.FindEntityType(typeof(Leg))!;

        var street = address.FindProperty(nameof(Address.Street))!.GetColumnName(StoreObjectIdentifier.Table("depots"));

        Assert.Equal("address_street", street);
        Assert.Equal(("legs", "driven_km"), (leg.GetTableName(), Column(leg, nameof(Leg.DrivenKm))));
    }

    [Fact]
    public void A_hierarchy_in_one_table_names_it_after_its_root_with_a_snake_case_discriminator()
    {
        var ibc = Named.FindEntityType(typeof(IbcNozzle))!;

        var discriminator = ibc.FindDiscriminatorProperty()!;

        Assert.Equal("nozzles", ibc.GetTableName());
        Assert.Equal("discriminator", discriminator.GetColumnName());
        Assert.Equal("litres_left", Column(ibc, nameof(IbcNozzle.LitresLeft)));
    }

    [Fact]
    public void A_many_to_many_join_table_is_named_after_its_join_type_and_its_keys_as_columns()
    {
        var join = Named.GetEntityTypes().Single(e => e.HasSharedClrType);

        var columns = join.GetProperties().Select(p => Column(join, p.Name)).Order(StringComparer.Ordinal);

        Assert.Equal("vehicle_vehicle_categories", join.GetTableName());
        Assert.Equal(["vehicle_categories_id", "vehicles_id"], columns);
        Assert.Equal("pk_vehicle_vehicle_categories", join.FindPrimaryKey()!.GetName());
    }

    [Fact]
    public void A_name_past_63_bytes_keeps_what_fits_and_ends_in_a_hash_of_the_whole()
    {
        var line = Named.FindEntityType(typeof(MonthlyExciseDutyReturnForAgriculturalDieselRefundClaimLine))!;

        var names = new[] { line.GetTableName()!, line.GetIndexes().Single().GetDatabaseName()! };

        Assert.All(names, n => Assert.True(System.Text.Encoding.UTF8.GetByteCount(n) <= 63, n));
        Assert.Matches("^monthly_excise_duty_return_for_agricultural_diesel_ref_[0-9a-f]{8}$", names[0]);
        Assert.Matches("^ix_monthly_excise_duty_return_for_agricultural_diesel_[0-9a-f]{8}$", names[1]);
    }

    [Fact]
    public void The_same_long_name_is_cut_the_same_way_every_time()
    {
        var name = "ix_" + new string('a', 80);

        var cut = (Generators.Shared.StoreNames.Fit(name), Generators.Shared.StoreNames.Fit(name));

        Assert.Equal(cut.Item1, cut.Item2);
        Assert.NotEqual(Generators.Shared.StoreNames.Fit(name + "b"), cut.Item1);
    }

    [Fact]
    public void A_name_stated_in_configure_wins_over_rasks()
    {
        using var legacy = new LegacyNamedContext(OptionsLayout.LikeAddDbContext<LegacyNamedContext>().UseSqlite("Data Source=:memory:").Options);
        var fueling = legacy.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Fueling))!;

        var names = (fueling.GetTableName(), Column(fueling, nameof(Fueling.PistolId)), fueling.FindPrimaryKey()!.GetName());

        Assert.Equal(("Fueling", "pistolId", "Fueling_pkey"), names);
    }

    [Fact]
    public async Task A_database_created_from_the_model_reads_and_writes_through_the_names()
    {
        await using var db = new RaskNamedContext(OptionsLayout.LikeAddDbContext<RaskNamedContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        db.Add(new Fueling { Pistol = new Pistol { Number = 1, Label = "A" }, CreatedAt = new DateTime(2026, 11, 27), Km = new Odometer(10, 42) });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var read = await db.Set<Fueling>().SingleAsync(f => f.Km.End == 42, TestContext.Current.CancellationToken);
        var sql = db.Set<Fueling>().Where(f => f.PistolId == 1).ToQueryString();

        Assert.Equal(10, read.Km.Start);
        Assert.Contains("FROM \"fuelings\" AS \"f\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"f\".\"pistol_id\" = 1", sql, StringComparison.Ordinal);
    }

    private static string? Column(IReadOnlyEntityType entityType, string property) =>
        entityType.FindProperty(property)!.GetColumnName(StoreObjectIdentifier.Table(entityType.GetTableName()!, entityType.GetSchema()));

    private static IModel Model()
    {
        using var db = new RaskNamedContext(OptionsLayout.LikeAddDbContext<RaskNamedContext>().UseSqlite("Data Source=:memory:").Options);
        return db.GetService<IDesignTimeModel>().Model;
    }
}
