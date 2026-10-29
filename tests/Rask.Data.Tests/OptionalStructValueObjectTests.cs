using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>A one-value value object that is a struct, with a rule.</summary>
public readonly record struct OfferDiscount(int Value)
{
    public const string OutOfRange = "A discount is between 0 and 100.";

    public static IEnumerable<string> Validate(int value) => value is < 0 or > 100 ? [OutOfRange] : [];
}

/// <summary>A one-value struct over text, so the column has a length.</summary>
public readonly record struct OfferCode(string Value);

/// <summary>An aggregate whose one-value struct value objects are OPTIONAL: declared <c>T?</c>.</summary>
public sealed class Offer : Aggregate<int>, IFormModel
{
    public const string CodeTaken = "This code is already in use.";

    private Offer() { }

    public string Title { get; private set; } = "";

    public OfferDiscount? Discount { get; private set; }

    public OfferCode? Code { get; private set; }

    public static Offer Titled(string title, int? discount = null, string? code = null) =>
        new()
        {
            Title = title,
            Discount = discount is { } value ? new OfferDiscount(value) : null,
            Code = code is null ? null : new OfferCode(code),
        };

    public static void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("Offers");
        builder.Property(o => o.Code).HasMaxLength(16);
        builder.HasIndex(o => o.Code).IsUnique(CodeTaken);
    }
}

/// <summary>A value object of several values, built through its members, that holds an optional one-value struct.</summary>
public sealed record OfferTerms
{
    private OfferTerms() { }

    public OfferDiscount? Early { get; private set; }

    public int Days { get; private set; }

    public static OfferTerms Of(int? early, int days) =>
        new() { Early = early is { } percent ? new OfferDiscount(percent) : null, Days = days };
}

/// <summary>An aggregate that holds the optional struct one level down.</summary>
public sealed class OfferPlan : Aggregate<int>, IFormModel
{
    private OfferPlan() { }

    public OfferTerms Terms { get; private set; } = OfferTerms.Of(null, 0);

    public static OfferPlan With(int? early) => new() { Terms = OfferTerms.Of(early, 30) };
}

/// <summary>
/// A one-value struct value object held as <c>T?</c> is a value object like any other, and optional: a nullable
/// column of the value it holds, a nullable field of that value on the form model, and a rule asked only when
/// the field is filled — what a nullable reference-type value object has always been.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class OptionalStructValueObjectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-optional-vo-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task The_optional_struct_is_one_nullable_column_of_the_value_it_holds_with_its_own_facets()
    {
        await using var database = await StartDatabaseAsync();
        var offer = database.Context.Model.FindEntityType(typeof(Offer))!;

        var discount = offer.FindProperty(nameof(Offer.Discount))!;
        var code = offer.FindProperty(nameof(Offer.Code))!;

        Assert.Equal("Discount", discount.GetColumnName());
        Assert.Equal(typeof(int), discount.GetValueConverter()!.ProviderClrType);
        Assert.True(discount.IsNullable);
        Assert.Equal(typeof(string), code.GetValueConverter()!.ProviderClrType);
        Assert.Equal(16, code.GetMaxLength());
        Assert.True(code.IsNullable);
    }

    [Fact]
    public async Task A_row_is_created_with_nothing_and_with_a_value_and_both_are_read_back()
    {
        await using var database = await StartDatabaseAsync();

        var bare = await Offer.Create(new OfferModel { Title = "Bare" }, cancellationToken: TestContext.Current.CancellationToken);
        var full = await Offer.Create(
            new OfferModel { Title = "Full", Discount = 15, Code = "SPRING" },
            cancellationToken: TestContext.Current.CancellationToken);

        var storedBare = (await database.Load<Offer>(bare.Id, TestContext.Current.CancellationToken))!;
        var storedFull = (await database.Load<Offer>(full.Id, TestContext.Current.CancellationToken))!;
        Assert.Null(storedBare.Discount);
        Assert.Null(storedBare.Code);
        Assert.Equal(new OfferDiscount(15), storedFull.Discount);
        Assert.Equal(new OfferCode("SPRING"), storedFull.Code);
    }

    [Fact]
    public async Task The_edit_form_carries_the_value_or_nothing_and_an_update_sets_it_and_clears_it()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SaveAsync(Offer.Titled("Ada", discount: 15, code: "A-1"));

        var filled = (await Offer.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        var cleared = new OfferModel { Title = "Ada", Discount = null, Code = null };
        await Offer.Update(id, cleared, cancellationToken: TestContext.Current.CancellationToken);
        var empty = (await Offer.Model(id, cancellationToken: TestContext.Current.CancellationToken))!;
        empty.Discount = 40;
        await Offer.Update(id, empty, cancellationToken: TestContext.Current.CancellationToken);
        var again = (await database.Load<Offer>(id, TestContext.Current.CancellationToken))!;

        Assert.Equal((15, "A-1"), (filled.Discount, filled.Code));
        Assert.Null(empty.Code);
        Assert.Equal(new OfferDiscount(40), again.Discount);
        Assert.Null(again.Code);
    }

    [Fact]
    public async Task The_read_face_shows_the_value_as_the_nullable_primitive_it_is()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Offer.Titled("Ada", discount: 15));
        await SaveAsync(Offer.Titled("Bob"));
        await SaveAsync(Offer.Titled("Cy", discount: 60));

        var discounted = await Offer.Where(o => o.Discount > 10).OrderBy(o => o.Title).Select(o => o.Title);
        var without = await Offer.Where(o => o.Discount == null).Select(o => o.Title);

        Assert.Equal(["Ada", "Cy"], discounted);
        Assert.Equal(["Bob"], without);
    }

    [Fact]
    public async Task A_filled_value_the_rule_refuses_is_refused_by_the_write_and_an_empty_one_is_not_asked()
    {
        await using var database = await StartDatabaseAsync();

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Offer.Create(new OfferModel { Title = "Ada", Discount = 140 }, cancellationToken: TestContext.Current.CancellationToken));
        var accepted = await Offer.Create(new OfferModel { Title = "Ada" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([("Discount", OfferDiscount.OutOfRange)], refused.Failures.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Null(accepted.Discount);
    }

    [Fact]
    public async Task A_unique_index_over_the_optional_column_lets_many_rows_hold_nothing_and_refuses_a_second_value()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Offer.Titled("Ada"));
        await SaveAsync(Offer.Titled("Bob"));
        await SaveAsync(Offer.Titled("Cy", code: "C-1"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => SaveAsync(Offer.Titled("Di", code: "C-1")));
        var fromAForm = await Assert.ThrowsAsync<RaskValidationException>(
            () => Offer.Create(new OfferModel { Title = "Ed", Code = "C-1" }, cancellationToken: TestContext.Current.CancellationToken));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Offer.CodeTaken, failure.Message);
        Assert.Equal(["Code"], failure.Fields);
        Assert.Equal([("Code", Offer.CodeTaken)], fromAForm.Failures.Select(f => (f.Fields.Single(), f.Message)));
        Assert.Equal(3, await Offer.Count(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Over_an_adopted_column_that_is_not_null_a_value_is_written_and_nothing_is_refused_by_the_database_itself()
    {
        await using var database = await StartDatabaseAsync();
        await database.Context.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE "Offers";
            CREATE TABLE "Offers" ("Id" INTEGER PRIMARY KEY AUTOINCREMENT, "Title" TEXT NOT NULL, "Discount" INTEGER NOT NULL, "Code" TEXT NULL);
            """,
            TestContext.Current.CancellationToken);

        var written = await Offer.Create(new OfferModel { Title = "Ada", Discount = 5 }, cancellationToken: TestContext.Current.CancellationToken);
        var refused = await Record.ExceptionAsync(
            () => Offer.Create(new OfferModel { Title = "Bob" }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(new OfferDiscount(5), written.Discount);
        // No rule is invented for a schema Rask did not make: the property says optional, the column disagrees,
        // and the database's own refusal comes through as it is.
        var failure = Assert.IsType<DbUpdateException>(refused);
        Assert.Contains("NOT NULL constraint failed: Offers.Discount", failure.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inside_a_value_object_built_through_its_members_it_is_stored_or_left_empty_and_asked_when_filled()
    {
        await using var database = await StartDatabaseAsync();
        var bare = OfferPlan.With(null);
        await bare.Save(cancellationToken: TestContext.Current.CancellationToken);

        var full = await OfferPlan.Create(
            new OfferPlanModel { Terms = new() { Early = 4, Days = 10 } }, cancellationToken: TestContext.Current.CancellationToken);
        var refused = await Assert.ThrowsAsync<RaskValidationException>(() => OfferPlan.Create(
            new OfferPlanModel { Terms = new() { Early = -1, Days = 10 } }, cancellationToken: TestContext.Current.CancellationToken));

        var storedBare = (await database.Load<OfferPlan>(bare.Id, TestContext.Current.CancellationToken))!;
        var storedFull = (await database.Load<OfferPlan>(full.Id, TestContext.Current.CancellationToken))!;
        Assert.Null(storedBare.Terms.Early);
        Assert.Equal(new OfferDiscount(4), storedFull.Terms.Early);
        Assert.Equal([("Terms.Early", OfferDiscount.OutOfRange)], refused.Failures.Select(f => (f.Fields.Single(), f.Message)));
    }

    private static async Task<int> SaveAsync(Offer offer)
    {
        await offer.Save(cancellationToken: TestContext.Current.CancellationToken);
        return offer.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
