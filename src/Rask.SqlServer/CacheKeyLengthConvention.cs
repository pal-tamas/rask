using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SqlServer;

/// <summary>
/// Fits <c>Rask.Cache</c>'s key inside SQL Server's index key limit.
/// </summary>
/// <remarks>
/// <para>
/// A clustered index key holds at most 900 bytes, and <c>nvarchar</c> spends two per character, so a string key
/// longer than 450 characters is one SQL Server creates with a warning and then refuses to insert into. EF's own
/// SQL Server mapping already caps an unconfigured string key at <c>nvarchar(450)</c> for exactly this reason —
/// but <c>CacheEntry.Key</c> is configured at 512 so that SQLite and PostgreSQL keep their room, and an explicit
/// length wins over the provider's default.
/// </para>
/// <para>
/// So the cap is applied here, by the SQL Server package, and only to that key: SQLite apps get no migration out
/// of it, and no application entity is ever silently re-sized. The type is matched by name to keep this package
/// free of a <c>Rask.Cache</c> dependency; a test pins the name.
/// </para>
/// </remarks>
internal sealed class CacheKeyLengthConvention : IModelFinalizingConvention
{
    internal const string CacheEntryTypeName = "Rask.Cache.CacheEntry";

    internal const int MaxKeyLength = 450;

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.ClrType.FullName != CacheEntryTypeName || entityType.FindPrimaryKey() is not { } key)
            {
                continue;
            }

            foreach (var property in key.Properties)
            {
                // The length was set explicitly by Rask.Cache's own configuration, which a convention-sourced
                // SetMaxLength would not override; the mutable surface sets it at explicit precedence.
                if (property.ClrType == typeof(string) && property.GetMaxLength() is > MaxKeyLength)
                {
                    ((IMutableProperty)property).SetMaxLength(MaxKeyLength);
                }
            }
        }
    }
}

/// <summary>Adds <see cref="CacheKeyLengthConvention"/> to the model's conventions.</summary>
internal sealed class RaskSqlServerConventionSetPlugin : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        conventionSet.Add(new CacheKeyLengthConvention());
        return conventionSet;
    }
}

/// <summary>
/// The options extension <c>UseRaskSqlServer</c> adds: it registers <see cref="RaskSqlServerConventionSetPlugin"/>
/// in EF's internal service provider, and carries the <see cref="SqlServerOptions"/> the connection interceptor
/// applies.
/// </summary>
/// <remarks>
/// Carrying the options HERE is what makes a second <c>UseRaskSqlServer</c> call override the first: EF keeps one
/// extension per type and each call replaces it, whereas an interceptor that captured its own options would stack
/// beside the next one and keep sending the first call's settings.
/// </remarks>
/// <param name="options">The validated settings of the call that added this extension.</param>
internal sealed class RaskSqlServerOptionsExtension(SqlServerOptions options) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>The settings the connection interceptor sends on each open.</summary>
    internal SqlServerOptions Options { get; } = options;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) =>
        new EntityFrameworkServicesBuilder(services).TryAdd<IConventionSetPlugin, RaskSqlServerConventionSetPlugin>();

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask SQL Server conventions ";

        // Every instance registers the same thing, so they can share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:SqlServerConventions"] = "1";
    }
}
