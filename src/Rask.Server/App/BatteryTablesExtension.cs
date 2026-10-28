using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask;

/// <summary>Carries which batteries are on into <see cref="RaskAppDbContext" />, through its options.</summary>
internal sealed class BatteryTablesExtension(BatteryTables tables) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    internal BatteryTables Tables { get; } = tables;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>The batteries <paramref name="options" /> says are on: every one, when it says nothing.</summary>
    internal static BatteryTables Of(DbContextOptions options) =>
        options.FindExtension<BatteryTablesExtension>()?.Tables ?? BatteryTables.All;

    // Added, not tried: the provider registers EF Core's own key factory first, and the last registration wins.
    public void ApplyServices(IServiceCollection services)
    {
        services.AddSingleton<IModelCacheKeyFactory, RaskAppModelCacheKeyFactory>();

        // Carried by every RaskAppDbContext, whichever provider: a collection of value objects turned NOT NULL is
        // filled with [] by its migration.
        RaskMigrationsModelDiffer.Register(services);
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(BatteryTablesExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"with battery tables {extension.Tables} ";

        // What it registers is the same for every switch, so they all share one internal service provider; the model is
        // told apart by RaskAppModelCacheKeyFactory instead.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:BatteryTables"] = extension.Tables.ToString();
    }
}
