using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;

namespace Rask.MySql;

/// <summary>
/// Applies the configured <see cref="MySqlOptions"/> session settings every time a MySQL connection is opened.
/// Registered by <c>UseRaskMySql(...)</c>.
/// </summary>
/// <remarks>
/// The settings are read from the context's <see cref="RaskMySqlOptionsExtension"/> at open time rather than
/// captured here, so the last <c>UseRaskMySql</c> call on a configuration is the one that takes effect — an
/// interceptor holding its own options would stack beside a second call's and keep sending the first call's values.
/// </remarks>
internal sealed class RaskMySqlConnectionInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (connection is MySqlConnection mySql && OptionsFor(eventData.Context) is { } options)
        {
            MySqlSessionSettings.Apply(mySql, options);
        }
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (connection is MySqlConnection mySql && OptionsFor(eventData.Context) is { } options)
        {
            await MySqlSessionSettings.ApplyAsync(mySql, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The settings of the last <c>UseRaskMySql</c> call on <paramref name="context"/>'s configuration.</summary>
    internal static MySqlOptions? OptionsFor(DbContext? context) =>
        context?.GetService<IDbContextOptions>().FindExtension<RaskMySqlOptionsExtension>()?.Options;
}

/// <summary>
/// The options extension <c>UseRaskMySql</c> adds: it registers <see cref="RaskMySqlConventionSetPlugin"/> in EF's
/// internal service provider, and carries the <see cref="MySqlOptions"/> the connection interceptor applies.
/// </summary>
/// <remarks>
/// Carrying the options HERE is what makes a second <c>UseRaskMySql</c> call override the first: EF keeps one
/// extension per type and each call replaces it.
/// </remarks>
/// <param name="options">The validated settings of the call that added this extension.</param>
internal sealed class RaskMySqlOptionsExtension(MySqlOptions options) : IDbContextOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;

    /// <summary>The settings the connection interceptor sends on each open.</summary>
    internal MySqlOptions Options { get; } = options;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services) =>
        new EntityFrameworkServicesBuilder(services).TryAdd<IConventionSetPlugin, RaskMySqlConventionSetPlugin>();

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo(IDbContextOptionsExtension extension) : DbContextOptionsExtensionInfo(extension)
    {
        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using Rask MySQL conventions ";

        // Every instance registers the same thing, so they can share an internal service provider.
        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["Rask:MySqlConventions"] = "1";
    }
}
