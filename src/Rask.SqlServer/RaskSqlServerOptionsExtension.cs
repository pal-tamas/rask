// EF1001: SqlServerCompiledQueryCacheKeyGenerator is the provider's own compiled-query cache key generator, in its Internal
// namespace; the zone is added to the key it makes (ZonedQueryCacheKeys), which needs it by its type.
#pragma warning disable EF1001

using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.SqlServer;

/// <summary>
/// The options extension <c>UseRaskSqlServer</c> adds: it registers <see cref="RaskSqlServerConventionSetPlugin"/>
/// in EF's internal service provider, translates <c>end - start</c> between two dates
/// (<see cref="SqlServerDurationTranslatorPlugin"/>), and carries the <see cref="SqlServerOptions"/> the connection interceptor
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

    // EF Core's services builder activates the plugin by reflection without a trimming annotation, so its constructor
    // is kept here.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(SqlServerDurationTranslatorPlugin))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(RaskSqlServerMigrationsSqlGenerator))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(SqlServerMomentDayTranslator))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ZonedQueryCacheKeys<SqlServerCompiledQueryCacheKeyGenerator>))]
    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, RaskSqlServerConventionSetPlugin>()
            .TryAdd<IMethodCallTranslatorPlugin, SqlServerDurationTranslatorPlugin>()
            .TryAdd<IMemberTranslatorPlugin, SqlServerDurationTranslatorPlugin>()
            .TryAdd<IMemberTranslatorPlugin, SqlServerMomentDayTranslator>();

        // `end - start` between two dates reaches the plugin above as a call it can translate.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor>(QueryDuration.Interceptor));

        // Carried by every UseRaskSqlServer context: a collection of value objects turned NOT NULL is filled with [].
        RaskMigrationsModelDiffer.Register(services);

        // A column of moments kept as the wall clock is rewritten to UTC as its migration applies (MomentsInUtc).
        // Added, not tried, as the differ is: the provider registers its own generator, and the last registration wins.
        services.AddScoped<IMigrationsSqlGenerator, RaskSqlServerMigrationsSqlGenerator>();

        // A moment's day is written with the zone in its SQL (SqlServerMomentDayTranslator), so the zone is a part of
        // every compiled query's cache key: a query run again under another zone is compiled again for it.
        ZonedQueryCacheKeys.Register<SqlServerCompiledQueryCacheKeyGenerator>(services);

        // An optional value object whose members are all optional is held in its row's columns (OptionalComplexValues).
        // Added, not tried: the provider registers its own validator, and the last registration wins.
        services.AddSingleton<IModelValidator, RaskSqlServerModelValidator>();
    }

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
