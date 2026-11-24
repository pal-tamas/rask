using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Data;

namespace Rask.SQLite;

/// <summary>
///     Says, to the model a <c>UseRaskSqlite</c> context builds, that every connection it opens registers the text
///     collation <c>OrderTextBy</c> names — so its text columns may be created with it.
/// </summary>
/// <remarks>
///     Its one service, <see cref="SqliteCollationInterceptor" />, does the registering on each open, from the annotation
///     the model then carries. <c>UseRaskSqlite</c> adds it, and so does <c>UseRaskFullTextSearch</c> on a browser's plain
///     <c>UseSqlite</c>; a <c>UseSqlite</c> with neither keeps its columns as they are.
/// </remarks>
internal sealed class TextCollationOptionsExtension : IDbContextOptionsExtension, ITextCollationEnforcer
{
    private DbContextOptionsExtensionInfo? _info;

    public string Collation => SqliteCollations.TextOrder;

    public string SearchCollation => SqliteCollations.SearchOrder;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInterceptor>(SqliteCollationInterceptor.Instance));
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IMethodCallTranslatorPlugin, SqliteSearchTranslatorPlugin>());
    }

    public void Validate(IDbContextOptions options)
    {
    }

    /// <summary>Adds the extension to <paramref name="optionsBuilder" />, once however often it is called.</summary>
    internal static void Add(DbContextOptionsBuilder optionsBuilder) =>
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(
            optionsBuilder.Options.FindExtension<TextCollationOptionsExtension>() ?? new TextCollationOptionsExtension());

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask SQLite text collation ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:SqliteTextCollation"] = "1";
    }
}
