using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>A tenant-scoped table whose unique indexes say what a collision means — and one that does not.</summary>
public sealed class Lane : Aggregate<int>
{
    public const string NameTaken = "Ilyen néven már létezik viszonylat.";
    public const string CodeTaken = "This code is already in use.";
    public const string RouteTaken = "A lane between these two places already exists.";

    private Lane() { }

    public const Timestamps Stamps = Timestamps.None;

    public const Concurrency Checks = Concurrency.None;

    public const Tenancy Scope = Tenancy.PerTenant;

    public int? TenantId { get; private set; }

    public string Name { get; private set; } = "";

    public string Code { get; private set; } = "";

    public string Origin { get; private set; } = "";

    public string Target { get; private set; } = "";

    public string Slug { get; private set; } = "";

    public static Lane Between(string origin, string target) => new()
    {
        Name = $"{origin}–{target}",
        Code = $"{origin}-{target}".ToUpperInvariant(),
        Origin = origin,
        Target = target,
        Slug = $"{origin}-{target}".ToLowerInvariant(),
    };

    public Lane With(string? name = null, string? code = null, string? slug = null)
    {
        Name = name ?? Name;
        Code = code ?? Code;
        Slug = slug ?? Slug;
        return this;
    }

    public static void Configure(EntityTypeBuilder<Lane> builder)
    {
        // Names the tenant, one column of its own: the message belongs under that field.
        builder.HasIndex(l => new { l.Name, l.TenantId }).IsUnique(NameTaken);

        // Does not name the tenant, so Rask puts it in front — and the message has to survive that.
        builder.HasIndex(l => l.Code).IsUnique(CodeTaken);

        // Two columns of its own: a rule about the row, not about either field.
        builder.HasIndex(l => new { l.Origin, l.Target }).IsUnique(RouteTaken);

        // No message: the provider's error, as it always was.
        builder.HasIndex(l => l.Slug).IsUnique();
    }
}

/// <summary>One table for everybody, with one unique column.</summary>
public sealed class Badge : Aggregate<int>
{
    public const string SerialTaken = "That serial number is already registered.";

    private Badge() { }

    public string Serial { get; private set; } = "";

    public static Badge Numbered(string serial) => new() { Serial = serial };

    // The two-step spelling, kept: IsUnique(message) is this and IsUnique() in one.
    public static void Configure(EntityTypeBuilder<Badge> builder) =>
        builder.HasIndex(b => b.Serial).IsUnique().HasViolationMessage(SerialTaken);
}

/// <summary>
/// A unique index that says what its violation means: a save that collides fails the way a validator's rule
/// does — a <see cref="RaskValidationException" /> carrying the message, under the field the index is over —
/// instead of as the provider's <see cref="DbUpdateException" />.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ViolationMessageTests : IDisposable
{
    private const int Acme = 7;
    private const int Globex = 8;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-violation-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_second_row_with_the_same_name_in_one_tenant_fails_with_the_message_under_that_field()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Lane.Between("Gyor", "Graz").With(name: "Budapest–Vienna")));

        Assert.Equal([Lane.NameTaken], refused.Errors["Name"]);
        Assert.Single(refused.Errors);
        Assert.IsType<DbUpdateException>(refused.InnerException);
    }

    [Fact]
    public async Task Another_tenant_can_hold_the_same_name()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        await SaveAsync(Globex, Lane.Between("Budapest", "Vienna"));

        using (Tenant.Across())
        {
            Assert.Equal(2, await Lane.Count(l => l.Name == "Budapest–Vienna", TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task The_message_survives_the_tenant_being_put_in_front_of_the_index()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Lane.Between("Gyor", "Graz").With(code: "BUDAPEST-VIENNA")));

        Assert.Equal([Lane.CodeTaken], refused.Errors["Code"]);
        Assert.Contains(
            database.Context.Model.FindEntityType(typeof(Lane))!.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "Code"]));
    }

    [Fact]
    public async Task An_index_over_several_columns_fails_with_the_message_about_the_row_as_a_whole()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Lane.Between("Budapest", "Vienna").With(name: "other", code: "OTHER", slug: "other")));

        Assert.Equal([Lane.RouteTaken], refused.Errors[""]);
        Assert.Single(refused.Errors);
    }

    [Fact]
    public async Task An_index_with_no_message_keeps_the_providers_own_error()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        var refused = await Assert.ThrowsAsync<DbUpdateException>(
            () => SaveAsync(Acme, Lane.Between("Gyor", "Graz").With(slug: "budapest-vienna")));

        Assert.Contains("UNIQUE", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rename_onto_a_name_that_is_taken_fails_with_the_same_message()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));
        var other = await SaveAsync(Acme, Lane.Between("Gyor", "Graz"));

        RaskValidationException refused;
        using (Tenant.Use(Acme))
        {
            refused = await Assert.ThrowsAsync<RaskValidationException>(() => Lane.Update(
                other, l => l.With(name: "Budapest–Vienna"), cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Equal([Lane.NameTaken], refused.Errors["Name"]);
    }

    [Fact]
    public async Task A_save_through_plain_ef_core_fails_with_the_message_too()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        RaskValidationException refused;
        using (Tenant.Use(Acme))
        {
            database.Context.Add(Lane.Between("Gyor", "Graz").With(name: "Budapest–Vienna"));
            refused = await Assert.ThrowsAsync<RaskValidationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal([Lane.NameTaken], refused.Errors["Name"]);
    }

    [Fact]
    public async Task What_the_caller_is_told_never_holds_the_value_that_collided()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna").With(name: "secret-of-acme"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Lane.Between("Gyor", "Graz").With(name: "secret-of-acme")));

        Assert.DoesNotContain("secret-of-acme", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(refused.Errors.SelectMany(e => e.Value), m => m.Contains("secret-of-acme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_row_that_collided_is_not_written_and_the_one_it_collided_with_is_untouched()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(Acme, Lane.Between("Budapest", "Vienna"));

        await Assert.ThrowsAsync<RaskValidationException>(
            () => SaveAsync(Acme, Lane.Between("Gyor", "Graz").With(name: "Budapest–Vienna")));

        using (Tenant.Use(Acme))
        {
            Assert.Equal(["BUDAPEST-VIENNA"], await Lane.Select(l => l.Code));
        }
    }

    [Fact]
    public async Task A_table_that_is_not_tenant_scoped_reports_its_one_column_the_same_way()
    {
        await using var database = await StartDatabaseAsync();
        await Badge.Numbered("A-1").Save(cancellationToken: TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Badge.Numbered("A-1").Save(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([Badge.SerialTaken], refused.Errors["Serial"]);
    }

    private static async Task<int> SaveAsync(int tenant, Lane lane)
    {
        using (Tenant.Use(tenant))
        {
            await lane.Save(cancellationToken: TestContext.Current.CancellationToken);
            return lane.Id;
        }
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
