using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

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

    public void ApplyServices(IServiceCollection services)
    {
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(BatteryTablesExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => $"with battery tables {extension.Tables} ";

        // It registers no service, so every switch shares one internal service provider; the model is told apart by
        // RaskAppModelCacheKeyFactory instead.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:BatteryTables"] = extension.Tables.ToString();
    }
}
