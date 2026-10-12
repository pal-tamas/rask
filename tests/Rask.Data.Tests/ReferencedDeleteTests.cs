using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rask.Cqrs;

namespace Rask.Data.Tests;

/// <summary>Referenced by a row the model maps: the relationship says what a refused delete means.</summary>
public sealed class Trailer : Aggregate<int>
{
    public const string OnADocket = "A pótkocsi nem törölhető, mert szállítólevél hivatkozik rá.";

    private Trailer() { }

    public string Plate { get; private set; } = "";

    public static Trailer Plated(string plate) => new() { Plate = plate };

    public static void Configure(EntityTypeBuilder<Trailer> builder) =>
        builder.HasMany<Docket>().WithOne()
            .HasForeignKey(d => d.TrailerId)
            .OnDelete(DeleteBehavior.Restrict, OnADocket);
}

public sealed class Docket : Aggregate<int>
{
    private Docket() { }

    public int TrailerId { get; private set; }

    public static Docket For(int trailerId) => new() { TrailerId = trailerId };
}

/// <summary>Referenced by a table the model does not map: the entity says it, for whatever references it.</summary>
public sealed class Tractor : Aggregate<int>
{
    public const string InUse = "A vontató nem törölhető, mert még hivatkoznak rá.";

    private Tractor() { }

    public string Plate { get; private set; } = "";

    public static Tractor Plated(string plate) => new() { Plate = plate };

    public static void Configure(EntityTypeBuilder<Tractor> builder) => builder.WhenReferenced(InUse);
}

/// <summary>Referenced, and says nothing about it.</summary>
public sealed class Garage : Aggregate<int>
{
    private Garage() { }

    public string Name { get; private set; } = "";

    public static Garage Named(string name) => new() { Name = name };

    public static void Configure(EntityTypeBuilder<Garage> builder) =>
        builder.HasMany<Bay>().WithOne().HasForeignKey(b => b.GarageId).OnDelete(DeleteBehavior.Restrict);
}

public sealed class Bay : Aggregate<int>
{
    private Bay() { }

    public int GarageId { get; private set; }

    public static Bay In(int garageId) => new() { GarageId = garageId };
}

/// <summary>Two relationships with a sentence each, and one for the entity: SQLite cannot say which refused.</summary>
public sealed class Yard : Aggregate<int>
{
    public const string HasAGate = "The yard still has a gate.";
    public const string HasAFence = "The yard still has a fence.";
    public const string InUse = "The yard is still in use.";

    private Yard() { }

    public string Name { get; private set; } = "";

    public static Yard Named(string name) => new() { Name = name };

    public static void Configure(EntityTypeBuilder<Yard> builder)
    {
        builder.HasMany<Gate>().WithOne().HasForeignKey(g => g.YardId).OnDelete(DeleteBehavior.Restrict, HasAGate);
        builder.HasMany<Fence>().WithOne().HasForeignKey(f => f.YardId).OnDelete(DeleteBehavior.Restrict, HasAFence);
        builder.WhenReferenced(InUse);
    }
}

public sealed class Gate : Aggregate<int>
{
    private Gate() { }

    public int YardId { get; private set; }

    public static Gate Of(int yardId) => new() { YardId = yardId };
}

public sealed class Fence : Aggregate<int>
{
    private Fence() { }

    public int YardId { get; private set; }

    public static Fence Of(int yardId) => new() { YardId = yardId };
}

/// <summary>Soft-deleted: the row stays, so the database is never asked.</summary>
public sealed class Forwarder : Aggregate<int>
{
    public const string HasAnAgreement = "The forwarder still has an agreement.";

    public const Deletion Deletes = Deletion.Soft;

    private Forwarder() { }

    public string Name { get; private set; } = "";

    public static Forwarder Named(string name) => new() { Name = name };

    public static void Configure(EntityTypeBuilder<Forwarder> builder) =>
        builder.HasMany<Agreement>().WithOne()
            .HasForeignKey(a => a.ForwarderId)
            .OnDelete(DeleteBehavior.Restrict, HasAnAgreement);
}

public sealed class Agreement : Aggregate<int>
{
    private Agreement() { }

    public int ForwarderId { get; private set; }

    public static Agreement With(int forwarderId) => new() { ForwarderId = forwarderId };
}

/// <summary>
/// A delete the database refuses because the row is still referenced fails with the sentence the model declared
/// — a <see cref="RaskValidationException" /> about the delete as a whole, naming no field — instead of as the
/// provider's <see cref="DbUpdateException" />. Without a sentence, or when it was not a delete, nothing changes.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ReferencedDeleteTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-referenced-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Deleting_a_row_a_mapped_row_references_fails_with_the_relationships_sentence()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Trailer.Delete(trailer.Id, cancellationToken: Cancellation));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Trailer.OnADocket, failure.Message);
        Assert.Empty(failure.Fields);
        Assert.Null(failure.Marked);
        Assert.Equal("FK_Docket_Trailer_TrailerId", failure.Source);
        Assert.Equal([Trailer.OnADocket], refused.Errors[""]);
        Assert.Single(refused.Errors);
        Assert.IsType<DbUpdateException>(refused.InnerException);
    }

    [Fact]
    public async Task The_row_whose_delete_was_refused_is_still_there()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);

        await Assert.ThrowsAsync<RaskValidationException>(
            () => Trailer.Delete(trailer.Id, cancellationToken: Cancellation));

        Assert.Equal(1, await Trailer.Count(t => t.Plate == "ABC-123", Cancellation));
    }

    [Fact]
    public async Task Deleting_a_row_nothing_references_deletes_it()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);

        await Trailer.Delete(trailer.Id, cancellationToken: Cancellation);

        Assert.Equal(0, await Trailer.Count(t => t.Plate == "ABC-123", Cancellation));
    }

    [Fact]
    public async Task Deleting_a_row_only_an_unmapped_table_references_fails_with_the_entitys_sentence()
    {
        await using var database = await StartDatabaseAsync();
        var tractor = Tractor.Plated("XYZ-789");
        await tractor.Save(cancellationToken: Cancellation);
        await database.Context.Database.ExecuteSqlAsync(
            $"""
             CREATE TABLE "TripLog" ("Id" INTEGER PRIMARY KEY, "TractorId" INTEGER NOT NULL REFERENCES "Tractor" ("Id"));
             INSERT INTO "TripLog" ("TractorId") VALUES ({tractor.Id});
             """,
            Cancellation);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Tractor.Delete(tractor.Id, cancellationToken: Cancellation));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Tractor.InUse, failure.Message);
        Assert.Empty(failure.Fields);
        Assert.Equal(nameof(Tractor), failure.Source);
    }

    [Fact]
    public async Task A_delete_through_plain_ef_core_fails_with_the_sentence_too()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);

        database.Context.Remove(await database.Context.Set<Trailer>().SingleAsync(Cancellation));
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => database.Context.SaveChangesAsync(Cancellation));

        Assert.Equal(Trailer.OnADocket, Assert.Single(refused.Failures).Message);
    }

    [Fact]
    public async Task A_generated_delete_in_a_context_that_tracks_the_referencing_row_fails_with_the_sentence_too()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);
        await database.Context.Set<Docket>().LoadAsync(Cancellation);

        // EF Core refuses this one itself, in Remove, before the database is asked.
        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Trailer.Delete(trailer.Id, db: database.Context, cancellationToken: Cancellation));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Trailer.OnADocket, failure.Message);
        Assert.Empty(failure.Fields);
        Assert.Equal("FK_Docket_Trailer_TrailerId", failure.Source);
        Assert.IsType<InvalidOperationException>(refused.InnerException);
    }

    [Fact]
    public async Task A_tracked_referencing_row_says_which_of_two_relationships_refused()
    {
        await using var database = await StartDatabaseAsync();
        var yard = Yard.Named("East");
        await yard.Save(cancellationToken: Cancellation);
        await Fence.Of(yard.Id).Save(cancellationToken: Cancellation);
        await database.Context.Set<Fence>().LoadAsync(Cancellation);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Yard.Delete(yard.Id, db: database.Context, cancellationToken: Cancellation));

        Assert.Equal(Yard.HasAFence, Assert.Single(refused.Failures).Message);
    }

    [Fact]
    public async Task A_generated_delete_with_a_tracked_referencing_row_and_no_sentence_keeps_ef_cores_own_error()
    {
        await using var database = await StartDatabaseAsync();
        var garage = Garage.Named("North");
        await garage.Save(cancellationToken: Cancellation);
        await Bay.In(garage.Id).Save(cancellationToken: Cancellation);
        await database.Context.Set<Bay>().LoadAsync(Cancellation);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Garage.Delete(garage.Id, db: database.Context, cancellationToken: Cancellation));

        Assert.Contains("severed", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plain_remove_with_a_tracked_referencing_row_is_ef_cores_own_refusal_and_is_not_translated()
    {
        await using var database = await StartDatabaseAsync();
        var trailer = Trailer.Plated("ABC-123");
        await trailer.Save(cancellationToken: Cancellation);
        await Docket.For(trailer.Id).Save(cancellationToken: Cancellation);
        await database.Context.Set<Docket>().LoadAsync(Cancellation);
        var tracked = await database.Context.Set<Trailer>().SingleAsync(Cancellation);

        // Remove itself throws: no save is attempted, so there is no failed save to translate.
        var refused = Record.Exception(() => database.Context.Remove(tracked));

        Assert.IsType<InvalidOperationException>(refused);
    }

    [Fact]
    public async Task A_relationship_with_no_sentence_keeps_the_providers_own_error()
    {
        await using var database = await StartDatabaseAsync();
        var garage = Garage.Named("North");
        await garage.Save(cancellationToken: Cancellation);
        await Bay.In(garage.Id).Save(cancellationToken: Cancellation);

        var refused = await Assert.ThrowsAsync<DbUpdateException>(
            () => Garage.Delete(garage.Id, cancellationToken: Cancellation));

        Assert.Contains("FOREIGN KEY", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_inserted_with_a_key_that_points_nowhere_keeps_the_providers_own_error()
    {
        await using var database = await StartDatabaseAsync();

        var refused = await Assert.ThrowsAsync<DbUpdateException>(
            () => Docket.For(404).Save(cancellationToken: Cancellation));

        Assert.Contains("FOREIGN KEY", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_relationships_that_could_have_refused_fall_back_to_the_entitys_sentence()
    {
        await using var database = await StartDatabaseAsync();
        var yard = Yard.Named("East");
        await yard.Save(cancellationToken: Cancellation);
        await Fence.Of(yard.Id).Save(cancellationToken: Cancellation);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Yard.Delete(yard.Id, cancellationToken: Cancellation));

        var failure = Assert.Single(refused.Failures);
        Assert.Equal(Yard.InUse, failure.Message);
        Assert.Equal(nameof(Yard), failure.Source);
    }

    [Fact]
    public async Task A_unique_index_in_the_same_context_still_fails_with_its_own_message()
    {
        await using var database = await StartDatabaseAsync();
        await Badge.Numbered("A-1").Save(cancellationToken: Cancellation);

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => Badge.Numbered("A-1").Save(cancellationToken: Cancellation));

        Assert.Equal([Badge.SerialTaken], refused.Errors["Serial"]);
    }

    [Fact]
    public async Task A_soft_deleted_row_is_never_refused_because_the_database_is_not_asked()
    {
        await using var database = await StartDatabaseAsync();
        var forwarder = Forwarder.Named("Acme");
        await forwarder.Save(cancellationToken: Cancellation);
        await Agreement.With(forwarder.Id).Save(cancellationToken: Cancellation);

        await Forwarder.Delete(forwarder.Id, cancellationToken: Cancellation);

        Assert.Equal(0, await Forwarder.Count(c => c.Name == "Acme", Cancellation));
        Assert.Equal(1, await Agreement.Count(a => a.ForwarderId == forwarder.Id, Cancellation));
    }

    [Fact]
    public void A_string_sets_the_delete_behaviour_and_records_the_sentence_on_the_foreign_key()
    {
        var trailer = new ModelBuilder().Entity<Trailer>();

        var relationship = trailer.HasMany<Docket>().WithOne()
            .HasForeignKey(d => d.TrailerId)
            .OnDelete(DeleteBehavior.NoAction, Trailer.OnADocket);

        Assert.Equal(DeleteBehavior.NoAction, relationship.Metadata.DeleteBehavior);
        Assert.Equal(Trailer.OnADocket, relationship.Metadata.FindAnnotation(ReferencedDelete.RelationshipAnnotation)?.Value);
    }

    [Fact]
    public void A_relationship_declared_from_the_referencing_side_records_the_same_sentence()
    {
        var docket = new ModelBuilder().Entity<Docket>();

        var relationship = docket.HasOne<Trailer>().WithMany()
            .HasForeignKey(d => d.TrailerId)
            .OnDelete(DeleteBehavior.Restrict, Trailer.OnADocket);

        Assert.Equal(DeleteBehavior.Restrict, relationship.Metadata.DeleteBehavior);
        Assert.Equal(Trailer.OnADocket, relationship.Metadata.FindAnnotation(ReferencedDelete.RelationshipAnnotation)?.Value);
    }

    [Fact]
    public void A_one_to_one_relationship_records_the_sentence_too()
    {
        var docket = new ModelBuilder().Entity<Docket>();

        var relationship = docket.HasOne<Trailer>().WithOne()
            .HasForeignKey<Docket>(d => d.TrailerId)
            .OnDelete(DeleteBehavior.Restrict, Trailer.OnADocket);

        Assert.Equal(DeleteBehavior.Restrict, relationship.Metadata.DeleteBehavior);
        Assert.Equal(Trailer.OnADocket, relationship.Metadata.FindAnnotation(ReferencedDelete.RelationshipAnnotation)?.Value);
    }

    [Fact]
    public void A_delete_behaviour_alone_is_still_ef_cores_and_records_no_sentence()
    {
        var garage = new ModelBuilder().Entity<Garage>();

        var relationship = garage.HasMany<Bay>().WithOne()
            .HasForeignKey(b => b.GarageId)
            .OnDelete(DeleteBehavior.Restrict);

        Assert.Equal(DeleteBehavior.Restrict, relationship.Metadata.DeleteBehavior);
        Assert.Null(relationship.Metadata.FindAnnotation(ReferencedDelete.RelationshipAnnotation));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_sentence_is_refused_where_it_is_declared(string blank)
    {
        var tractor = new ModelBuilder().Entity<Tractor>();
        var relationship = new ModelBuilder().Entity<Trailer>().HasMany<Docket>().WithOne();

        var onTheEntity = Record.Exception(() => tractor.WhenReferenced(blank));
        var onTheRelationship = Record.Exception(() => relationship.OnDelete(DeleteBehavior.Restrict, blank));

        Assert.IsType<ArgumentException>(onTheEntity);
        Assert.IsType<ArgumentException>(onTheRelationship);
    }

    [Fact]
    public async Task A_constraint_postgresql_names_is_answered_by_that_relationships_sentence()
    {
        await using var database = await StartDatabaseAsync();
        var refusal = Refusing(database.Context, Yard.Named("East"), EntityState.Deleted, Postgres("FK_Fence_Yard_YardId"));

        var translated = ReferencedDelete.Translate(refusal);

        var failure = Assert.Single(translated!.Failures);
        Assert.Equal(Yard.HasAFence, failure.Message);
        Assert.Equal("FK_Fence_Yard_YardId", failure.Source);
    }

    [Fact]
    public async Task A_constraint_sql_server_quotes_is_answered_by_that_relationships_sentence()
    {
        await using var database = await StartDatabaseAsync();
        var refusal = Refusing(database.Context, Yard.Named("East"), EntityState.Deleted, SqlServer("FK_Gate_Yard_YardId"));

        var translated = ReferencedDelete.Translate(refusal);

        var failure = Assert.Single(translated!.Failures);
        Assert.Equal(Yard.HasAGate, failure.Message);
        Assert.Equal("FK_Gate_Yard_YardId", failure.Source);
    }

    [Fact]
    public async Task A_named_constraint_the_model_does_not_know_is_answered_by_the_entitys_sentence()
    {
        await using var database = await StartDatabaseAsync();
        var refusal = Refusing(database.Context, Yard.Named("East"), EntityState.Deleted, Postgres("FK_Survey_Yard"));

        var translated = ReferencedDelete.Translate(refusal);

        var failure = Assert.Single(translated!.Failures);
        Assert.Equal(Yard.InUse, failure.Message);
        Assert.Equal("FK_Survey_Yard", failure.Source);
    }

    [Fact]
    public async Task A_named_constraint_the_model_does_not_know_never_borrows_a_relationships_sentence()
    {
        await using var database = await StartDatabaseAsync();
        var refusal = Refusing(database.Context, Trailer.Plated("ABC-123"), EntityState.Deleted, SqlServer("FK_Survey_Trailer"));

        var translated = ReferencedDelete.Translate(refusal);

        Assert.Null(translated);
    }

    [Fact]
    public async Task A_named_constraint_that_refused_an_insert_is_not_a_refused_delete()
    {
        await using var database = await StartDatabaseAsync();
        var refusal = Refusing(database.Context, Yard.Named("East"), EntityState.Added, Postgres("FK_Fence_Yard_YardId"));

        var translated = ReferencedDelete.Translate(refusal);

        Assert.Null(translated);
    }

    // What a provider's refusal looks like to the translation, without the provider: the entries of the
    // command that failed, and the provider's exception inside EF Core's.
    private static DbUpdateException Refusing(DbContext context, object row, EntityState state, DbException provider)
    {
        var entry = context.Entry(row);
        entry.State = state;
        return new DbUpdateException("The save failed.", provider, [entry]);
    }

    private static ProviderRefusal Postgres(string constraint)
    {
        var refusal = new ProviderRefusal($"23503: violates foreign key constraint \"{constraint}\"", "23503");
        refusal.Data["ConstraintName"] = constraint;
        return refusal;
    }

    private static ProviderRefusal SqlServer(string constraint)
    {
        var refusal = new ProviderRefusal(
            $"The DELETE statement conflicted with the REFERENCE constraint \"{constraint}\". " +
            "The conflict occurred in database \"fleet\", table \"dbo.Gate\", column 'YardId'.");
        refusal.Data["HelpLink.EvtID"] = "547";
        return refusal;
    }

    private sealed class ProviderRefusal(string message, string? sqlState = null) : DbException(message)
    {
        public override string? SqlState { get; } = sqlState;
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"));
}
