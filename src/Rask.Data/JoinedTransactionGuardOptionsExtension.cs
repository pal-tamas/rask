using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rask.Data;

/// <summary>Puts <see cref="JoinedTransactionManager" /> in front of the transaction manager EF Core gives a context.</summary>
/// <remarks>
///     Replace, not TryAdd: the provider registers its own, before this or after it. A provider that has already
///     registered a manager but no relational connection keeps its own: there is no connection to join there.
/// </remarks>
internal sealed class JoinedTransactionGuardOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Adds the extension to <paramref name="optionsBuilder" />, once.</summary>
    internal static void Add(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.Options.FindExtension<JoinedTransactionGuardOptionsExtension>() is null)
        {
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
                new JoinedTransactionGuardOptionsExtension());
        }
    }

    public void ApplyServices(IServiceCollection services)
    {
        if (services.Any(static s => s.ServiceType == typeof(IDbContextTransactionManager)) &&
            !services.Any(static s => s.ServiceType == typeof(IRelationalConnection)))
        {
            return;
        }

        services.Replace(ServiceDescriptor.Scoped<IDbContextTransactionManager>(static p => new JoinedTransactionManager(
            p.GetRequiredService<IRelationalConnection>(), p.GetRequiredService<ICurrentDbContext>().Context)));
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:JoinedTransactionGuard"] = "1";
    }
}
