using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
/// The options extension <c>UseRaskSqlite</c> adds for full-text search: it registers, in EF Core's internal service
/// provider, the convention mapping each index as a queryable entity, the <c>highlight</c>/<c>snippet</c>
/// translator, and the interceptor that rewrites <c>Search</c>.
/// </summary>
/// <remarks>
/// Registering them as services rather than through <c>AddInterceptors</c> is what makes a second
/// <c>UseRaskSqlite</c> call harmless: EF keeps one extension per type, so the rewrite runs once however many times
/// the context is configured.
/// </remarks>
internal sealed class FullTextSearchOptionsExtension : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    // EF Core's services builder activates both plugins by reflection without a trimming annotation, so their
    // constructors are kept here; the interceptor's ServiceDescriptor is annotated and needs nothing.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(FullTextSearchConventionSetPlugin))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(FullTextFunctionTranslatorPlugin))]
    public void ApplyServices(IServiceCollection services)
    {
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IConventionSetPlugin, FullTextSearchConventionSetPlugin>()
            .TryAdd<IMethodCallTranslatorPlugin, FullTextFunctionTranslatorPlugin>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor, FullTextSearchQueryInterceptor>());
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask SQLite full-text search ";

        // Every instance registers the same thing, so they can share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:SqliteFullTextSearch"] = "1";
    }
}
