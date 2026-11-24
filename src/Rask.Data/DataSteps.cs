using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Rask.Data;

/// <summary>
///     Runs the data steps (<see cref="DataMigrationExtensions.Data" />) of every applied migration whose steps have not
///     run yet, in migration order, once the schema is up to date — and records each, so it runs once.
/// </summary>
/// <remarks>
///     <para>
///         After the schema rather than between its migrations: a step is C# over the model the app has now, which only
///         the fully migrated schema matches. A migration's steps are found by building its <c>Up</c>, as EF Core does to
///         apply it; a database whose migrations declare none is never touched by this — not even the record's table is made.
///     </para>
///     <para>
///         The record is a table of Rask's beside EF Core's history, <c>__RaskDataSteps</c>, made by the first migration
///         run that has a step to record, and read by every later one: a migration applied by <c>dotnet ef</c>, which runs
///         no C#, has its steps run the next time the app migrates.
///     </para>
/// </remarks>
internal static class DataSteps
{
    /// <summary>The table each migration whose steps have run is recorded in.</summary>
    internal const string Table = "__RaskDataSteps";

    private const string Column = "MigrationId";

    /// <summary>Runs the steps not yet run of the migrations applied to <paramref name="db" />'s database.</summary>
    /// <param name="db">The migrated context; its connection records each migration whose steps ran.</param>
    /// <param name="cancellationToken">Cancels between steps.</param>
    /// <returns>The ids of the migrations whose steps ran, in order.</returns>
    internal static async Task<IReadOnlyList<string>> RunPendingAsync(DbContext db, CancellationToken cancellationToken)
    {
        var declared = Declared(db);
        if (declared.Count == 0)
        {
            return [];
        }

        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var due = declared.Where(m => applied.Contains(m.Id)).ToList();
        if (due.Count == 0)
        {
            return [];
        }

        var sql = db.GetService<ISqlGenerationHelper>();
        await db.Database.ExecuteSqlRawAsync(CreateTable(db.Database.ProviderName, sql), cancellationToken).ConfigureAwait(false);
        var done = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var id in StoredText.Read(db, $"SELECT {sql.DelimitIdentifier(Column)} FROM {sql.DelimitIdentifier(Table)}", cancellationToken)
                           .ConfigureAwait(false))
        {
            done.Add(id);
        }

        var ran = new List<string>();
        foreach (var (id, steps) in due.Where(m => !done.Contains(m.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A migration is the whole database's: every tenant's rows, as a step's UpdateEach means them.
            using (Tenant.Across())
            {
                foreach (var step in steps)
                {
                    await step().ConfigureAwait(false);
                }
            }

#pragma warning disable EF1002, S2077 // the names are Rask's own constants, quoted by the provider; the id goes as a parameter
            await db.Database.ExecuteSqlRawAsync(
                $"INSERT INTO {sql.DelimitIdentifier(Table)} ({sql.DelimitIdentifier(Column)}) VALUES ({{0}})", [id], cancellationToken)
                .ConfigureAwait(false);
#pragma warning restore EF1002, S2077
            ran.Add(id);
        }

        return ran;
    }

    // Each migration of the context's assembly that declares a step, in the order they apply.
    private static List<(string Id, List<Func<Task>> Steps)> Declared(DbContext db)
    {
        var migrations = db.GetService<IMigrationsAssembly>();
        var provider = db.Database.ProviderName ?? "";
        var declared = new List<(string, List<Func<Task>>)>();
        foreach (var (id, type) in migrations.Migrations)
        {
            var steps = migrations.CreateMigration(type, provider).UpOperations.Select(DataStep.Of).OfType<Func<Task>>().ToList();
            if (steps.Count > 0)
            {
                declared.Add((id, steps));
            }
        }

        return declared;
    }

    // Made when missing, in each provider's own words for that.
    private static string CreateTable(string? provider, ISqlGenerationHelper sql)
    {
        var table = sql.DelimitIdentifier(Table);
        var column = sql.DelimitIdentifier(Column);
        return string.Equals(provider, ProviderNames.SqlServer, StringComparison.Ordinal)
            ? $"IF OBJECT_ID(N'{Table}') IS NULL CREATE TABLE {table} ({column} nvarchar(150) NOT NULL PRIMARY KEY);"
            : $"CREATE TABLE IF NOT EXISTS {table} ({column} varchar(150) NOT NULL PRIMARY KEY);";
    }
}
