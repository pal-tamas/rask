using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data;

/// <summary>How a <see cref="TestDatabase" /> brings its schema up before the test runs.</summary>
internal static class SchemaStep
{
    /// <summary>Creates the schema straight from the model — the database a model-only test needs.</summary>
    public static Task Create(DbContext context, CancellationToken cancellationToken) =>
        context.Database.EnsureCreatedAsync(cancellationToken);

    /// <summary>
    ///     Applies the app's migrations when it has any — the schema production runs on — and otherwise creates it from
    ///     the model. On a standing database that is already current, neither changes anything.
    /// </summary>
    [RequiresUnreferencedCode(DataTrimming.EfCoreUnreferencedCode)]
    public static Task Migrate(DbContext context, CancellationToken cancellationToken) =>
        context.Database.GetMigrations().Any()
            ? context.Database.MigrateAsync(cancellationToken)
            : context.Database.EnsureCreatedAsync(cancellationToken);
}
