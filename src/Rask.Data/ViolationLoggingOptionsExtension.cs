using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.Data;

/// <summary>Puts <see cref="ViolationLogging" /> in front of the logger factory EF Core logs through.</summary>
/// <remarks>
///     It wraps the factory EF Core itself would pick — the one <c>UseLoggerFactory</c> named, else the app's — so
///     every other entry reaches the same loggers it always did.
/// </remarks>
internal sealed class ViolationLoggingOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Adds the extension to <paramref name="optionsBuilder" />, once.</summary>
    internal static void Add(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.Options.FindExtension<ViolationLoggingOptionsExtension>() is null)
        {
            ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
                new ViolationLoggingOptionsExtension());
        }
    }

    // Replace, not TryAdd: the provider may already have registered EF Core's own factory.
    public void ApplyServices(IServiceCollection services)
    {
        services.AddScoped(static internalServices => new ViolationLogging(AppLoggerFactory(internalServices)));
        services.Replace(ServiceDescriptor.Scoped<ILoggerFactory>(static p => p.GetRequiredService<ViolationLogging>()));
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private static ILoggerFactory AppLoggerFactory(IServiceProvider internalServices)
    {
        var core = internalServices.GetService<IDbContextOptions>()?.FindExtension<CoreOptionsExtension>();

        return core?.LoggerFactory
            ?? core?.ApplicationServiceProvider?.GetService<ILoggerFactory>()
            ?? NullLoggerFactory.Instance;
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:ViolationLogging"] = "1";
    }
}
