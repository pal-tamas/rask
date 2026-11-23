using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// The options extension <c>UseRaskSqlite</c> adds so a query can use <c>end - start</c> between two dates: the
/// rewrite that hands the subtraction to <see cref="SqliteDurationTranslatorPlugin"/>, and the plugin itself.
/// </summary>
/// <remarks>
/// Services rather than an <c>AddInterceptors</c> call, so a second <c>UseRaskSqlite</c> registers nothing twice: EF
/// keeps one extension per type.
/// </remarks>
internal sealed class SqliteDurationOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    /// <summary>Adds the extension to <paramref name="optionsBuilder" />, once.</summary>
    internal static void Add(DbContextOptionsBuilder optionsBuilder) =>
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            optionsBuilder.Options.FindExtension<SqliteDurationOptionsExtension>() ?? new SqliteDurationOptionsExtension());

    // EF Core's services builder activates the plugin by reflection without a trimming annotation, so its constructor
    // is kept here.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(SqliteDurationTranslatorPlugin))]
    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, SqliteDurationTranslatorPlugin>()
            .TryAdd<IMemberTranslatorPlugin, SqliteDurationTranslatorPlugin>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor>(QueryDuration.Interceptor));
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask SQLite durations ";

        // Every instance registers the same thing, so they can share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:SqliteDurations"] = "1";
    }
}
