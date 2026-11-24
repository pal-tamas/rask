using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite;

/// <summary>
///     The options extension <c>UseRaskSqlite</c> adds so a <see cref="DateTimeOffset" /> compares by its instant and a
///     <see cref="decimal" /> is kept at its scale: it registers <see cref="SqliteColumnConventionSetPlugin" /> in EF
///     Core's internal service provider.
/// </summary>
/// <remarks>
///     A service rather than a call on the model, so a second <c>UseRaskSqlite</c> registers nothing twice; a plain
///     <c>UseSqlite</c> (a browser app's) has neither the extension nor the moment collation on its connections, and its
///     columns stay as EF Core makes them.
/// </remarks>
internal sealed class SqliteColumnOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Adds the extension to <paramref name="optionsBuilder" />, once.</summary>
    internal static void Add(DbContextOptionsBuilder optionsBuilder) =>
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            optionsBuilder.Options.FindExtension<SqliteColumnOptionsExtension>() ?? new SqliteColumnOptionsExtension());

    // EF Core's services builder activates the plugin by reflection without a trimming annotation, so its constructor
    // is kept here.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(SqliteColumnConventionSetPlugin))]
    public void ApplyServices(IServiceCollection services) =>
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, SqliteColumnConventionSetPlugin>();

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask SQLite column conventions ";

        // Every instance registers the same thing, so they can share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:SqliteColumns"] = "1";
    }
}
