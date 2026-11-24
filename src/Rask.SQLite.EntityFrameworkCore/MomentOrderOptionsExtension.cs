using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite;

/// <summary>
///     The options extension <c>UseRaskSqlite</c> adds so a <see cref="DateTimeOffset" /> compares by its instant: it
///     registers <see cref="MomentOrderConventionSetPlugin" /> in EF Core's internal service provider.
/// </summary>
/// <remarks>
///     A service rather than a call on the model, so a second <c>UseRaskSqlite</c> registers nothing twice; a plain
///     <c>UseSqlite</c> (a browser app's) has neither the extension nor the collation on its connections, and its
///     columns stay as EF Core makes them.
/// </remarks>
internal sealed class MomentOrderOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Adds the extension to <paramref name="optionsBuilder" />, once.</summary>
    internal static void Add(DbContextOptionsBuilder optionsBuilder) =>
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            optionsBuilder.Options.FindExtension<MomentOrderOptionsExtension>() ?? new MomentOrderOptionsExtension());

    // EF Core's services builder activates the plugin by reflection without a trimming annotation, so its constructor
    // is kept here.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(MomentOrderConventionSetPlugin))]
    public void ApplyServices(IServiceCollection services) =>
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, MomentOrderConventionSetPlugin>();

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask SQLite moment order ";

        // Every instance registers the same thing, so they can share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:SqliteMomentOrder"] = "1";
    }
}
