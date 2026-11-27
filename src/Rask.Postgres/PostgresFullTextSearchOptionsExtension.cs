// EF1001: NpgsqlCompiledQueryCacheKeyGenerator is the provider's own compiled-query cache key generator, in its Internal
// namespace; the zone is added to the key it makes (ZonedQueryCacheKeys), which needs it by its type.
#pragma warning disable EF1001

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql.EntityFrameworkCore.PostgreSQL.Query.Internal;
using NpgsqlTypes;
using Rask.Data;

namespace Rask.Postgres;

/// <summary>
///     Registers the model and query halves of PostgreSQL full-text search on a context, and stores a <c>Moment</c> value
///     object as UTC (<see cref="PostgresMomentConventionSetPlugin" />) and writes <c>Min</c>/<c>Max</c> of a <c>uuid</c>
///     (<see cref="PostgresUuidAggregatePlugin" />).
/// </summary>
internal sealed class PostgresFullTextSearchOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    // EF Core's services builder activates the translator by reflection without a trimming annotation.
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, typeof(MomentDayTranslator))]
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors,
        typeof(ZonedQueryCacheKeys<NpgsqlCompiledQueryCacheKeyGenerator>))]
    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, PostgresFullTextSearchConventionSetPlugin>()
            .TryAdd<IConventionSetPlugin, PostgresMomentConventionSetPlugin>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor, PostgresFullTextSearchQueryInterceptor>());

        // Min and Max of a uuid, which PostgreSQL has no aggregate for.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAggregateMethodCallTranslatorPlugin, PostgresUuidAggregatePlugin>());

        // A moment's day on the wall calendar, read in a query off its UTC column (MomentDayTranslator).
        services.TryAddScoped<IMomentDays, PostgresMomentDays>();
        new EntityFrameworkRelationalServicesBuilder(services).TryAdd<IMemberTranslatorPlugin, MomentDayTranslator>();

        // That day is written with the zone in its SQL, so the zone is a part of every compiled query's cache key: a
        // query run again under another zone is compiled again for it.
        ZonedQueryCacheKeys.Register<NpgsqlCompiledQueryCacheKeyGenerator>(services);

        // Carried by every UseRaskPostgres context: a collection of value objects turned NOT NULL is filled with [].
        RaskMigrationsModelDiffer.Register(services);

        // An optional value object whose members are all optional is held in its row's columns (OptionalComplexValues).
        // Added, not tried: the provider registers its own validator, and the last registration wins.
        services.AddSingleton<IModelValidator, RaskPostgresModelValidator>();
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask PostgreSQL full-text search ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:PostgresFullTextSearch"] = "1";
    }
}
