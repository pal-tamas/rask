namespace Rask.Cli.Scaffolding;

/// <summary>The database a scaffolded app is wired to, chosen with <c>rask new --database</c>.</summary>
/// <remarks>
/// SQLite is the default and stays the default: it needs no server, and for a single-developer product it
/// carries the whole app — jobs, mail, cache and the outbox all ride the same file. The others are the door
/// out of one box, for when a managed database, a read replica, or more app instances than one file can
/// serve is genuinely the requirement.
/// </remarks>
internal enum DatabaseProvider
{
    /// <summary>SQLite — one file on local disk, Rask's default.</summary>
    Sqlite,

    /// <summary>PostgreSQL, via <c>Rask.Postgres</c>.</summary>
    Postgres,

    /// <summary>Microsoft SQL Server, via <c>Rask.SqlServer</c>.</summary>
    SqlServer,
}

/// <summary>
/// A database provider's scaffolding facts: its <see cref="Key"/> (what the user types after
/// <c>rask new --database</c>), a human <see cref="DisplayName"/>, the package and <c>Use…</c> call the
/// generated <c>Program.cs</c> needs, and whether the file-based operations apply.
/// </summary>
/// <param name="Key">The value accepted by <c>--database</c>.</param>
/// <param name="Provider">The enum this entry describes.</param>
/// <param name="ShortName">The engine's name on its own, for prose ("EF Core + PostgreSQL").</param>
/// <param name="DisplayName">How the provider is named in prompts and help, with its trade-off.</param>
/// <param name="Package">The Rask package that supplies <see cref="UseMethod"/>.</param>
/// <param name="Namespace">The namespace the generated <c>Program.cs</c> must import.</param>
/// <param name="UseMethod">The <c>DbContextOptionsBuilder</c> extension the generated wiring calls.</param>
/// <param name="DefaultConnectionString">The fallback used when no <c>ConnectionStrings:App</c> is configured.</param>
/// <param name="EfPackage">The EF Core provider package a generated test project needs directly.</param>
/// <param name="TestUseMethod">The plain EF <c>Use…</c> call a generated persistence test uses.</param>
internal sealed record DatabaseInfo(
    string Key,
    DatabaseProvider Provider,
    string ShortName,
    string DisplayName,
    string Package,
    string Namespace,
    string UseMethod,
    string DefaultConnectionString,
    string EfPackage,
    string TestUseMethod)
{
    /// <summary>
    /// True when the database is a single local file, so Litestream continuous backup, scheduled file
    /// snapshots, and the file-copy <c>rask db backup</c> all mean something.
    /// </summary>
    /// <remarks>
    /// This is the one question the rest of the scaffolder asks about a provider, and it is deliberately
    /// phrased as a capability rather than <c>Provider == Sqlite</c>: the batteries that depend on it depend
    /// on "the database is a file I can copy", not on the name of the engine.
    /// </remarks>
    public bool IsFileBased => Provider == DatabaseProvider.Sqlite;
}

/// <summary>
/// The database providers <c>rask new</c> can wire, kept in one place so the command, the generators and
/// the tests read the same source of truth — the same role <see cref="Templates.TemplateCatalog"/> plays
/// for templates.
/// </summary>
internal static class DatabaseCatalog
{
    public static IReadOnlyList<DatabaseInfo> All { get; } =
    [
        new(
            Key: "sqlite",
            Provider: DatabaseProvider.Sqlite,
            ShortName: "SQLite",
            DisplayName: "SQLite (one file, no server)",
            Package: "Rask.SQLite.EntityFrameworkCore",
            Namespace: "Rask.SQLite",
            UseMethod: "UseRaskSqlite",
            DefaultConnectionString: "Data Source=app.db",
            EfPackage: "Microsoft.EntityFrameworkCore.Sqlite",
            TestUseMethod: "UseSqlite"),
        new(
            Key: "postgres",
            Provider: DatabaseProvider.Postgres,
            ShortName: "PostgreSQL",
            DisplayName: "PostgreSQL (a server you run or rent)",
            Package: "Rask.Postgres",
            Namespace: "Rask.Postgres",
            UseMethod: "UseRaskPostgres",
            DefaultConnectionString: "Host=localhost;Database=app;Username=postgres;Password=postgres",
            EfPackage: "Npgsql.EntityFrameworkCore.PostgreSQL",
            TestUseMethod: "UseNpgsql"),
        new(
            Key: "sqlserver",
            Provider: DatabaseProvider.SqlServer,
            ShortName: "SQL Server",
            DisplayName: "SQL Server (a server you run or rent)",
            Package: "Rask.SqlServer",
            Namespace: "Rask.SqlServer",
            UseMethod: "UseRaskSqlServer",
            DefaultConnectionString: "Server=localhost;Database=app;User Id=sa;Password=Your_password123;TrustServerCertificate=true",
            EfPackage: "Microsoft.EntityFrameworkCore.SqlServer",
            TestUseMethod: "UseSqlServer"),
    ];

    /// <summary>The default when <c>--database</c> is omitted — SQLite, and deliberately so.</summary>
    public static DatabaseInfo Default => All[0];

    /// <summary>The accepted <c>--database</c> values, for help text and error messages.</summary>
    public static string Keys => string.Join("|", All.Select(database => database.Key));

    public static DatabaseInfo For(DatabaseProvider provider)
        => All.First(database => database.Provider == provider);

    /// <summary>
    /// Works out which database a project uses from its <c>.csproj</c> text, by looking for the Rask package
    /// that supplies the <c>Use…</c> call. Falls back to the default when nothing matches — a project with no
    /// database package yet is about to have SQLite wired into it.
    /// </summary>
    /// <remarks>
    /// Matched on the <c>Include="…"</c> attribute rather than a bare substring so a comment mentioning a
    /// package, or a longer id that merely starts with one, can't decide the answer. The non-default
    /// providers are checked first: <c>Rask.SQLite.EntityFrameworkCore</c> is the fallback anyway, so an app
    /// that somehow references both is treated as the more specific one rather than silently as SQLite.
    /// </remarks>
    public static DatabaseProvider DetectProvider(string csprojText)
    {
        ArgumentNullException.ThrowIfNull(csprojText);

        foreach (var database in All.Where(d => d.Provider != Default.Provider))
        {
            if (csprojText.Contains($"Include=\"{database.Package}\"", StringComparison.OrdinalIgnoreCase))
            {
                return database.Provider;
            }
        }

        return Default.Provider;
    }

    public static bool TryGet(string key, out DatabaseInfo database)
    {
        foreach (var candidate in All)
        {
            if (candidate.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                database = candidate;
                return true;
            }
        }

        database = Default;
        return false;
    }
}
