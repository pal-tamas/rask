using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data;

/// <summary>
///     One model's empty schema, made once and copied into every test database over that model through SQLite's backup
///     API — so a test starts with tables in a fraction of a millisecond, however many the model has, instead of having
///     <c>EnsureCreated</c> work out and run the whole DDL again.
/// </summary>
/// <remarks>
///     The first database over a model creates its schema itself, then copies it here, before any test row is written.
///     What <c>EnsureCreated</c> leaves in the file comes along: tables, indexes, triggers, the full-text tables, the
///     <c>HasData</c> seed. Keyed by the model EF Core built, so a model built again — another context type, a registry
///     that grew — gets a template of its own. Each lives as long as the process, one private in-memory database apiece.
/// </remarks>
// CA1001: a template is never let go of — it lives in a static table for the whole test run, and its semaphore and
// connection go with the process, which is the point of keeping it.
#pragma warning disable CA1001
internal sealed class SqliteSchemaTemplate
#pragma warning restore CA1001
{
    private static readonly ConditionalWeakTable<IModel, SqliteSchemaTemplate> ByModel = new();

    private readonly SemaphoreSlim _first = new(1, 1);
    private readonly Lock _copying = new();
    private SqliteConnection? _schema;

    /// <summary>Gives <paramref name="database" /> — held open on <paramref name="context" />'s database — its schema.</summary>
    public static Task CreateSchema(DbContext context, SqliteConnection database, CancellationToken cancellationToken) =>
        ByModel.GetValue(context.Model, static _ => new SqliteSchemaTemplate()).CopyInto(context, database, cancellationToken);

    private async Task CopyInto(DbContext context, SqliteConnection database, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _schema) is { } made)
        {
            Copy(made, database);
            return;
        }

        await _first.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_schema is { } madeMeanwhile)
            {
                Copy(madeMeanwhile, database);
                return;
            }

            await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            // Private and in memory: nothing else names it, and it stays as long as this connection does.
            var schema = new SqliteConnection("Data Source=:memory:");
            await schema.OpenAsync(cancellationToken).ConfigureAwait(false);
            Copy(database, schema);
            Volatile.Write(ref _schema, schema);
        }
        finally
        {
            _first.Release();
        }
    }

    // One copy at a time: a connection is not to be used from two threads at once, and the template's is shared by every test.
    private void Copy(SqliteConnection from, SqliteConnection to)
    {
        lock (_copying)
        {
            from.BackupDatabase(to);
        }
    }
}
