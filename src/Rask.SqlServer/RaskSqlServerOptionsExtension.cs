using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SqlServer;

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
