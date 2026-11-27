using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     Says, to the model a <c>UseRaskPostgres</c> context builds, that its migrations create the text collation
///     <c>OrderTextBy</c> names — a nondeterministic ICU collation, <see cref="Name" /> — so its text columns may use it.
/// </summary>
/// <remarks>
///     <see cref="RaskPostgresMigrationsSqlGenerator" /> writes the <c>CREATE COLLATION</c> before the first table that
///     needs it, and <see cref="PostgresTextTranslatorPlugin" /> writes the string operations PostgreSQL refuses on it. A plain <c>UseNpgsql</c> has neither, and
///     orders by the language's ICU collation alone.
/// </remarks>
internal sealed class PostgresTextCollationOptionsExtension : IDbContextOptionsExtension, ITextCollationEnforcer
{
    /// <summary>The collation's name in the schema.</summary>
    internal const string Name = "rask_text";

    private DbContextOptionsExtensionInfo? _info;

    public string Collation => Name;

    public string SearchCollation => PostgresSearchCollation.Name;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    // The string operations PostgreSQL refuses on such a column, written in a form it runs.
    public void ApplyServices(IServiceCollection services) =>
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMethodCallTranslatorPlugin, PostgresTextTranslatorPlugin>());

    public void Validate(IDbContextOptions options)
    {
    }

    /// <summary>Adds the extension to <paramref name="optionsBuilder" />, once however often it is called.</summary>
    internal static void Add(DbContextOptionsBuilder optionsBuilder) =>
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            optionsBuilder.Options.FindExtension<PostgresTextCollationOptionsExtension>() ?? new PostgresTextCollationOptionsExtension());

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask PostgreSQL text collation ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:PostgresTextCollation"] = "1";
    }
}
