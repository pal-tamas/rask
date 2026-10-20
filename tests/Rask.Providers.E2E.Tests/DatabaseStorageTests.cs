using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Rask.Storage;

namespace Rask.Providers.E2E.Tests;

/// <summary>A tenant-scoped table whose tenants are numbered, which makes the stored-file tenant an <c>int</c>.</summary>
public sealed class Consignment : Aggregate<Guid, Timestamps, PerTenant<int>>
{
    private Consignment() { }

    public string Reference { get; private set; } = "";
}

/// <summary>An adopted app's own context: plain names in <c>dbo</c>, created by hand, no Rask migration.</summary>
public sealed class NumberedFilesContext(DbContextOptions<NumberedFilesContext> options) : DbContext(options), ITenantScoped
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Consignment>().ToTable("Consignments");
        modelBuilder.AddRaskStorage();
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>An app with no numbered tenants: the stored-file tenant stays a <c>Guid</c>.</summary>
public sealed class GuidFilesContext(DbContextOptions<GuidFilesContext> options) : DbContext(options), ITenantScoped
{
    public const string Schema = "rask_files";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.AddRaskStorage();
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>The same numbered app on PostgreSQL, in a schema of its own.</summary>
public sealed class PostgresNumberedFilesContext(DbContextOptions<PostgresNumberedFilesContext> options)
    : DbContext(options), ITenantScoped
{
    public const string Schema = "rask_files_numbered";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Consignment>().ToTable("Consignments");
        modelBuilder.AddRaskStorage();
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
/// <c>Rask__Storage__Provider=Database</c> against a real SQL Server: the tables created by hand from the DDL in
/// <c>docs/file-storage.md</c>, a file of a few megabytes streamed in and out, ranges, tenants and delete.
/// </summary>
/// <remarks>
/// The numbered app is the adopted shape: <c>dbo.Tenants</c> with an <c>int</c> key, and a foreign key from
/// <c>StoredFile.TenantId</c> that the app added itself. The Guid app runs on <c>UseRaskSqlServer</c>, whose
/// retrying execution strategy a save's transaction has to run inside.
/// </remarks>
[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDatabaseStorageTests : IAsyncLifetime
{
    private const string NumberedDatabase = "rask_e2e_storage_int";
    private const string GuidDatabase = "rask_e2e_storage_guid";

    public async ValueTask InitializeAsync()
    {
        if (!SqlServer.Available)
        {
            return;
        }

        await RecreateAsync(NumberedDatabase, """
            CREATE TABLE dbo.Tenants (Id int IDENTITY CONSTRAINT PK_Tenants PRIMARY KEY, Name nvarchar(255) NOT NULL);
            INSERT INTO dbo.Tenants (Name) VALUES (N'Acme'), (N'Globex');
            CREATE TABLE dbo.Consignments (Id uniqueidentifier NOT NULL PRIMARY KEY, Reference nvarchar(max) NOT NULL,
                TenantId int NULL, CreatedAt datetime2 NOT NULL, UpdatedAt datetime2 NOT NULL);
            """ + Ddl("sqlserver-int") + """

            ALTER TABLE dbo.StoredFile ADD CONSTRAINT FK_StoredFile_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants (Id);
            """);
        await RecreateAsync(GuidDatabase, Ddl("sqlserver-guid").Replace("dbo.", GuidFilesContext.Schema + ".", StringComparison.Ordinal),
            $"CREATE SCHEMA {GuidFilesContext.Schema};");
    }

    public async ValueTask DisposeAsync()
    {
        if (SqlServer.Available)
        {
            SqlConnection.ClearAllPools();
            await DropAsync(NumberedDatabase);
            await DropAsync(GuidDatabase);
        }
    }

    [Fact]
    public async Task A_numbered_app_keeps_a_file_in_the_tables_it_made_by_hand()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = StorageApp.Build<NumberedFilesContext>(o => o.UseSqlServer(SqlServer.Database(NumberedDatabase)));
        var files = app.GetRequiredService<IFiles>();
        var bytes = StorageApp.Pdf(3 * 1024 * 1024);

        StoredFile file;
        using (Tenant.Use(1))
        {
            file = await files.Save(new MemoryStream(bytes), "invoice.pdf", TestContext.Current.CancellationToken);
            Assert.Equal(bytes, await StorageApp.ReadAsync(files, file.Id));
            Assert.Equal(bytes[1_000_000..1_000_100], await StorageApp.DownloadRangeAsync(app, files, file.Id, 1_000_000, 1_000_099));
        }

        Assert.Equal(1, await ScalarAsync(NumberedDatabase, "SELECT TenantId FROM dbo.StoredFile WHERE Id = @id", file.Id));
        Assert.Equal(bytes.Length, await ScalarAsync(NumberedDatabase, "SELECT DATALENGTH(Bytes) FROM dbo.StoredFileBytes WHERE Id = @id", file.Id));
        using (Tenant.Use(2))
        {
            Assert.Null(await files.Get(file.Id, TestContext.Current.CancellationToken));
            Assert.False(await files.Delete(file.Id, TestContext.Current.CancellationToken));
        }

        using (Tenant.Use(1))
        {
            Assert.True(await files.Delete(file.Id, TestContext.Current.CancellationToken));
        }

        Assert.Null(await ScalarAsync(NumberedDatabase, "SELECT COUNT(*) FROM dbo.StoredFileBytes WHERE Id = @id HAVING COUNT(*) > 0", file.Id));
    }

    [Fact]
    public async Task A_save_the_apps_own_foreign_key_refuses_leaves_no_bytes_behind()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = StorageApp.Build<NumberedFilesContext>(o => o.UseSqlServer(SqlServer.Database(NumberedDatabase)));
        var files = app.GetRequiredService<IFiles>();
        var before = await ScalarAsync(NumberedDatabase, "SELECT COUNT(*) FROM dbo.StoredFileBytes", Guid.Empty);

        using (Tenant.Use(999))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => files.Save(new MemoryStream(StorageApp.Pdf(20_000)), "orphan.pdf", TestContext.Current.CancellationToken).AsTask());
        }

        Assert.Equal(before, await ScalarAsync(NumberedDatabase, "SELECT COUNT(*) FROM dbo.StoredFileBytes", Guid.Empty));
    }

    [Fact]
    public async Task A_guid_app_keeps_a_file_through_the_retrying_strategy()
    {
        Assert.SkipUnless(SqlServer.Available, SqlServer.SkipReason);
        await using var app = StorageApp.Build<GuidFilesContext>(o => o.UseRaskSqlServerAt(SqlServer.Database(GuidDatabase)));
        var files = app.GetRequiredService<IFiles>();
        var bytes = StorageApp.Pdf(2 * 1024 * 1024);
        var tenant = Guid.NewGuid();

        using (Tenant.Use(tenant))
        {
            var file = await files.Save(new MemoryStream(bytes), "scan.pdf", TestContext.Current.CancellationToken);

            Assert.Equal(bytes, await StorageApp.ReadAsync(files, file.Id));
            Assert.Equal(tenant, file.TenantId);
            Assert.True(await files.Delete(file.Id, TestContext.Current.CancellationToken));
        }
    }

    // The SQL a docs block holds, marked <!-- ddl:{name} --> above its fence.
    private static string Ddl(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Rask.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var docs = File.ReadAllText(Path.Combine(root.FullName, "docs", "file-storage.md"));
        var match = Regex.Match(docs, $@"<!-- ddl:{Regex.Escape(name)} -->\s*```sql\n(?<sql>.*?)```", RegexOptions.Singleline);
        Assert.True(match.Success, $"docs/file-storage.md has no ddl:{name} block.");
        return match.Groups["sql"].Value;
    }

    private static async Task RecreateAsync(string database, string ddl, string? first = null)
    {
        await DropAsync(database);
        await using (var master = new SqlConnection(SqlServer.Database("master")))
        {
            await master.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{database}]", master) { CommandTimeout = 300 };
            await create.ExecuteNonQueryAsync();
        }

        await using var connection = new SqlConnection(SqlServer.Database(database));
        await connection.OpenAsync();
        foreach (var batch in new[] { first, ddl }.OfType<string>())
        {
            // Schema work on a busy shared runner outlasts the 30-second default.
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 300 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task DropAsync(string database)
    {
        await using var master = new SqlConnection(SqlServer.Database("master"));
        await master.OpenAsync();
        await using var drop = new SqlCommand(
            $"IF DB_ID('{database}') IS NOT NULL BEGIN ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END",
            master)
        {
            CommandTimeout = 300,
        };
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(string database, string sql, Guid id)
    {
        await using var connection = new SqlConnection(SqlServer.Database(database));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value is long length ? (int)length : value;
    }
}

/// <summary>The same on a real PostgreSQL: a Guid app and a numbered one, each in a schema of its own.</summary>
[Collection(PostgresCollection.Name)]
public sealed class PostgresDatabaseStorageTests
{
    [Fact]
    public async Task A_guid_app_streams_a_file_in_and_out_of_bytea()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = StorageApp.Build<GuidFilesContext>(o => o.UseRaskPostgresAt(Postgres.Required));
        await ResetAsync<GuidFilesContext>(app, GuidFilesContext.Schema);
        var files = app.GetRequiredService<IFiles>();
        var bytes = StorageApp.Pdf(3 * 1024 * 1024);

        var file = await files.Save(new MemoryStream(bytes), "scan.pdf", TestContext.Current.CancellationToken);

        Assert.Equal(bytes, await StorageApp.ReadAsync(files, file.Id));
        Assert.Equal(bytes[2_000_000..2_000_050], await StorageApp.DownloadRangeAsync(app, files, file.Id, 2_000_000, 2_000_049));
        Assert.True(await files.Delete(file.Id, TestContext.Current.CancellationToken));
        Assert.Null(await files.OpenRead(file.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_numbered_app_keeps_an_integer_tenant_and_another_tenant_cannot_reach_the_file()
    {
        Assert.SkipUnless(Postgres.Available, Postgres.SkipReason);
        await using var app = StorageApp.Build<PostgresNumberedFilesContext>(o => o.UseRaskPostgresAt(Postgres.Required));
        await ResetAsync<PostgresNumberedFilesContext>(app, PostgresNumberedFilesContext.Schema);
        var files = app.GetRequiredService<IFiles>();

        StoredFile file;
        using (Tenant.Use(7))
        {
            file = await files.Save(new MemoryStream(StorageApp.Pdf(100_000)), "a.pdf", TestContext.Current.CancellationToken);
        }

        await using (var db = await app.GetRequiredService<IDbContextFactory<PostgresNumberedFilesContext>>()
                         .CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            var column = db.Model.FindEntityType(typeof(StoredFile))!.FindProperty(Columns.TenantId)!.GetColumnType();
            Assert.Equal("integer", column);
        }

        using (Tenant.Use(8))
        {
            Assert.Null(await files.Get(file.Id, TestContext.Current.CancellationToken));
        }

        using (Tenant.Use(7))
        {
            Assert.NotNull(await files.Get(file.Id, TestContext.Current.CancellationToken));
        }
    }

    private static async Task ResetAsync<TContext>(ServiceProvider app, string schema)
        where TContext : DbContext
    {
        await using var db = await app.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContextAsync();
        await Postgres.ResetSchemaAsync(db, schema);
    }
}

/// <summary>A hand-wired storage host over one context, its bytes in the database.</summary>
internal static class StorageApp
{
    internal static ServiceProvider Build<TContext>(Action<DbContextOptionsBuilder> provider)
        where TContext : DbContext
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddRaskStorage<TContext>(o => o.Provider = StorageProvider.Database);
        services.AddDbContextFactory<TContext>(provider);
        return services.BuildServiceProvider();
    }

    /// <summary><paramref name="size"/> bytes the sniffer takes for a PDF.</summary>
    internal static byte[] Pdf(int size)
    {
        var bytes = new byte[size];
        Random.Shared.NextBytes(bytes);
        "%PDF-1.7\n"u8.CopyTo(bytes);
        return bytes;
    }

    internal static async Task<byte[]> ReadAsync(IFiles files, Guid id)
    {
        await using var stream = await files.OpenRead(id, TestContext.Current.CancellationToken);
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, TestContext.Current.CancellationToken);
        return copy.ToArray();
    }

    // Download(id) answered as the file routes answer it, for one range.
    internal static async Task<byte[]> DownloadRangeAsync(ServiceProvider app, IFiles files, Guid id, long from, long to)
    {
        await using var scope = app.CreateAsyncScope();
        using var body = new MemoryStream();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = HttpMethods.Get;
        http.Request.Headers.Range = $"bytes={from}-{to}";
        http.Response.Body = body;

        await files.Download(id).ExecuteAsync(http);

        Assert.Equal(StatusCodes.Status206PartialContent, http.Response.StatusCode);
        return body.ToArray();
    }
}
