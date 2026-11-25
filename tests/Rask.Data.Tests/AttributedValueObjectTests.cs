using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
/// DataAnnotations on a property that holds a value object: [MaxLength], [StringLength] and [Precision] give its
/// column their own facet over the value object's, [Required] makes it NOT NULL, and the generated model carries each
/// attribute, so a form and a write ask it beside the value object's rule.
/// </summary>
public sealed class AttributedValueObjectTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task An_attribute_on_the_property_gives_the_column_its_facet_over_the_value_objects()
    {
        await using var database = await StartDatabaseAsync();

        var type = database.Context.Model.FindEntityType(typeof(Granary))!;

        Assert.Equal(50, type.FindProperty(nameof(Granary.Keeper))!.GetMaxLength());
        Assert.Equal(10, type.FindProperty(nameof(Granary.Deputy))!.GetMaxLength());
        Assert.Equal((12, 3), (type.FindProperty(nameof(Granary.Stock))!.GetPrecision(), type.FindProperty(nameof(Granary.Stock))!.GetScale()));
        Assert.False(type.FindProperty(nameof(Granary.Badge))!.IsNullable);
        Assert.Equal(RigLimits.Chip, type.FindProperty(nameof(Granary.Badge))!.GetMaxLength());
    }

    [Fact]
    public void The_generated_model_carries_each_attribute_of_the_value_object_property()
    {
        var model = typeof(GranaryModel);

        var keeper = model.GetProperty(nameof(GranaryModel.Keeper))!;
        var deputy = model.GetProperty(nameof(GranaryModel.Deputy))!;
        var badge = model.GetProperty(nameof(GranaryModel.Badge))!;

        Assert.Equal(50, Assert.Single(keeper.GetCustomAttributes(typeof(MaxLengthAttribute), false).Cast<MaxLengthAttribute>()).Length);
        Assert.Equal(10, Assert.Single(deputy.GetCustomAttributes(typeof(StringLengthAttribute), false).Cast<StringLengthAttribute>()).MaximumLength);
        Assert.Single(badge.GetCustomAttributes(typeof(RequiredAttribute), false));
    }

    [Fact]
    public async Task The_generated_write_asks_the_attribute_and_the_value_objects_rule_alike()
    {
        await using var database = await StartDatabaseAsync();
        var token = TestContext.Current.CancellationToken;

        var tighterAttribute = await Assert.ThrowsAsync<RaskValidationException>(
            () => Granary.Create(Model(deputy: new string('x', 11)), cancellationToken: token));
        var tighterRule = await Assert.ThrowsAsync<RaskValidationException>(
            () => Granary.Create(Model(keeper: new string('x', 21)), cancellationToken: token));
        var missing = await Assert.ThrowsAsync<RaskValidationException>(
            () => Granary.Create(Model(badge: null), cancellationToken: token));
        var created = await Granary.Create(Model(), cancellationToken: token);

        Assert.Contains("10", Assert.Single(Failures(tighterAttribute), f => f.Field == "Deputy").Message, StringComparison.Ordinal);
        Assert.Equal([("Keeper", "Enter at most 20 characters.")], Failures(tighterRule));
        Assert.Equal(["Badge"], Failures(missing).Select(f => f.Field));
        Assert.Equal("B1", (await database.Load<Granary>(created.Id, token))!.Badge!.Value);
    }

    private static GranaryModel Model(string keeper = "Ada", string deputy = "Bea", string? badge = "B1") =>
        new() { Keeper = keeper, Deputy = deputy, Stock = 5, Badge = badge };

    private static IEnumerable<(string Field, string Message)> Failures(RaskValidationException refused) =>
        refused.Failures.Select(f => (f.Fields.Single(), f.Message));

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
