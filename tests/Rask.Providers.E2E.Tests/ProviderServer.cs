using System.Runtime.InteropServices;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Rask.Providers.E2E.Tests.ProviderServers))]

namespace Rask.Providers.E2E.Tests;

/// <summary>
///     One database server a provider leg runs against: a container of its own on Testcontainers, started the first
///     time a fact asks for it and shared by every class of the run. There is no hand-started server to point it at.
/// </summary>
/// <remarks>
///     <para>
///         The container starts in CI (<c>CI</c> / <c>GITHUB_ACTIONS</c>), or where <c>RASK_PROVIDERS_LOCAL=1</c> says
///         Docker may be used. Anywhere else the facts report SKIPPED with the reason — a dev machine never starts a
///         container unasked, and its tests never need one: everything provider-independent runs on SQLite.
///     </para>
///     <para>
///         <c>RASK_PROVIDERS_SERVERS</c> (a comma list: <c>postgres</c>, <c>sqlserver</c>) narrows which servers this
///         process may use. scripts/run-providers-local.sh runs SQL Server in a process of its own, because EF Core refuses
///         a process its twenty-first internal service provider and the three providers together are past twenty.
///     </para>
///     <para>
///         <c>RASK_PROVIDERS_POSTGRES=floor</c> runs the PostgreSQL facts on the oldest server Rask supports instead of
///         the newest: the script runs them once on each, so the floor docs/data.md names stays proven.
///     </para>
///     <para>
///         A container that fails to start FAILS the facts that need it rather than skipping them: where one was asked
///         for, a missing server is a broken gate, not an absent one.
///     </para>
/// </remarks>
internal sealed class ProviderServer : IAsyncDisposable
{
    internal const string LocalVariable = "RASK_PROVIDERS_LOCAL";

    internal const string ServersVariable = "RASK_PROVIDERS_SERVERS";

    internal const string PostgresVersionVariable = "RASK_PROVIDERS_POSTGRES";

    // The newest PostgreSQL, and the oldest Rask supports: 18, the first whose LIKE takes a nondeterministic collation
    // (PostgresTextCollation refuses an older server). scripts/upstream/images.py moves the newest across majors and
    // the floor within its own.
    private const string PostgresNewest = "postgres:19.0-alpine";
    private const string PostgresFloor = "postgres:18.7-alpine";

    private readonly Lazy<string> _connectionString;
    private IDatabaseContainer? _container;

    private ProviderServer(string name, Func<IDatabaseContainer> container, string? hostCannot = null)
    {
        SkipReason = Skip(name, hostCannot);
        _connectionString = new Lazy<string>(() => Start(container));
    }

    /// <summary>SQL Server 2022, the image CI ran by hand before; amd64 only.</summary>
    internal static ProviderServer SqlServer { get; } = new(
        "sqlserver",
        static () => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build(),
        // Microsoft publishes the image for amd64 only, and on Apple Silicon it SEGFAULTS under emulation (exit 139 from
        // launch_sqlservr.sh, confirmed 2026-09-11): no point starting it there.
        RuntimeInformation.OSArchitecture == Architecture.X64
            ? null
            : $"the SQL Server image is amd64-only and crashes under emulation on {RuntimeInformation.OSArchitecture}");

    /// <summary>
    ///     PostgreSQL 19, or 18 under <c>RASK_PROVIDERS_POSTGRES=floor</c>: never older, as the text collations need 18.
    ///     Multi-arch.
    /// </summary>
    internal static ProviderServer Postgres { get; } = new(
        "postgres",
        static () => new PostgreSqlBuilder(PostgresImage).WithDatabase("rask").Build());

    private static string PostgresImage =>
        Environment.GetEnvironmentVariable(PostgresVersionVariable) switch
        {
            null or "" or "newest" => PostgresNewest,
            "floor" => PostgresFloor,
            var other => throw new InvalidOperationException(
                $"{PostgresVersionVariable}={other}: say newest (the default) or floor."),
        };

    /// <summary>Why the facts on this server are skipped, or <see langword="null" /> when it is there to run against.</summary>
    internal string? SkipReason { get; }

    internal bool Available => SkipReason is null;

    /// <summary>The server's connection string, its container started on the first call.</summary>
    internal string ConnectionString => _connectionString.Value;

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static string? Skip(string name, string? hostCannot)
    {
        if (Environment.GetEnvironmentVariable(ServersVariable) is { Length: > 0 } servers
            && !servers.Split(',', StringSplitOptions.TrimEntries).Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return $"{ServersVariable}={servers}: this process does not run against {name}.";
        }

        if (!InCi && Environment.GetEnvironmentVariable(LocalVariable) != "1")
        {
            return $"Runs in CI on Testcontainers; set {LocalVariable}=1 to run here (needs Docker).";
        }

        return hostCannot is null ? null : $"Not run on this host: {hostCannot}. CI runs it.";
    }

    private static bool InCi =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

    private string Start(Func<IDatabaseContainer> create)
    {
        var container = create();
        _container = container;
        // A Lazy's factory is synchronous; the start runs on the pool so no test's context is blocked under it.
        Task.Run(() => container.StartAsync()).GetAwaiter().GetResult();
        return container.GetConnectionString();
    }
}

/// <summary>Stops the servers this run started, once every class is done.</summary>
public sealed class ProviderServers : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await ProviderServer.SqlServer.DisposeAsync();
        await ProviderServer.Postgres.DisposeAsync();
    }
}
