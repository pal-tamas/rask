using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Rask.Data;

namespace Rask.Storage.Tests;

/// <summary>
/// The stored-file table of an app whose tenants are Guids is the table it always was — pinned, column for column, on
/// every provider — and an app whose tenant-scoped tables number their tenants keeps the number.
/// </summary>
/// <remarks>
/// The pinned scripts are the ones <c>modelBuilder.AddRaskStorage()</c> produced before <c>StoredFileBytes</c> and the
/// numbered tenant column existed. An existing Guid app must need no migration and no conversion: any difference here
/// is one an adopted database created by hand would fail on.
/// </remarks>
[Collection(StorageDbCollection.Name)]
public sealed class StoredFileTableTests
{
    private const string SqlServerTable = """
        CREATE TABLE [StoredFile] (
            [Id] uniqueidentifier NOT NULL,
            [Name] nvarchar(255) NOT NULL,
            [ContentType] nvarchar(127) NOT NULL,
            [Size] bigint NOT NULL,
            [Sha256] nvarchar(64) NOT NULL,
            [Provider] nvarchar(16) NOT NULL,
            [Key] nvarchar(512) NOT NULL,
            [Public] bit NOT NULL,
            [TenantId] uniqueidentifier NULL,
            [CreatedAt] datetime2 NOT NULL,
            [UpdatedAt] datetime2 NOT NULL,
            CONSTRAINT [PK_StoredFile] PRIMARY KEY ([Id])
        );
        GO


        CREATE INDEX [IX_StoredFile_CreatedAt] ON [StoredFile] ([CreatedAt]);
        GO
        """;

    private const string SqliteTable = """
        CREATE TABLE "StoredFile" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_StoredFile" PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "ContentType" TEXT NOT NULL,
            "Size" INTEGER NOT NULL,
            "Sha256" TEXT NOT NULL,
            "Provider" TEXT NOT NULL,
            "Key" TEXT NOT NULL,
            "Public" INTEGER NOT NULL,
            "TenantId" TEXT NULL,
            "CreatedAt" TEXT NOT NULL,
            "UpdatedAt" TEXT NOT NULL
        );


        CREATE INDEX "IX_StoredFile_CreatedAt" ON "StoredFile" ("CreatedAt");
        """;

    private const string PostgresTable = """
        CREATE TABLE "StoredFile" (
            "Id" uuid NOT NULL,
            "Name" character varying(255) NOT NULL,
            "ContentType" character varying(127) NOT NULL,
            "Size" bigint NOT NULL,
            "Sha256" character varying(64) NOT NULL,
            "Provider" character varying(16) NOT NULL,
            "Key" character varying(512) NOT NULL,
            "Public" boolean NOT NULL,
            "TenantId" uuid,
            "CreatedAt" timestamp with time zone NOT NULL,
            "UpdatedAt" timestamp with time zone NOT NULL,
            CONSTRAINT "PK_StoredFile" PRIMARY KEY ("Id")
        );


        CREATE INDEX "IX_StoredFile_CreatedAt" ON "StoredFile" ("CreatedAt");
        """;

    private const string SqlServerModel = """
        EntityType: StoredFile
          Properties:
            Id (Guid) Required PK AfterSave:Throw ValueGenerated.OnAdd
              Annotations:
                SqlServer:ValueGenerationStrategy: None
            ContentType (string) Required MaxLength(127)
              Annotations:
                MaxLength: 127
                SqlServer:ValueGenerationStrategy: None
            CreatedAt (DateTime) Required Index
              Annotations:
                SqlServer:ValueGenerationStrategy: None
            Key (string) Required MaxLength(512)
              Annotations:
                MaxLength: 512
                SqlServer:ValueGenerationStrategy: None
            Name (string) Required MaxLength(255)
              Annotations:
                MaxLength: 255
                SqlServer:ValueGenerationStrategy: None
            Provider (StorageProvider) Required MaxLength(16)
              Annotations:
                MaxLength: 16
                ProviderClrType: System.String
                SqlServer:ValueGenerationStrategy: None
            Public (bool) Required
              Annotations:
                SqlServer:ValueGenerationStrategy: None
            Sha256 (string) Required MaxLength(64)
              Annotations:
                MaxLength: 64
                SqlServer:ValueGenerationStrategy: None
            Size (long) Required
              Annotations:
                SqlServer:ValueGenerationStrategy: None
            TenantId (Guid?)
              Annotations:
                SqlServer:ValueGenerationStrategy: None
            UpdatedAt (DateTime) Required
              Annotations:
                SqlServer:ValueGenerationStrategy: None
          Keys:
            Id PK
          Indexes:
            CreatedAt
          Annotations:
            Relational:FunctionName:
            Relational:Schema:
            Relational:SqlQuery:
            Relational:TableName: StoredFile
            Relational:ViewName:
            Relational:ViewSchema:
        """;

    [Fact]
    public void A_guid_app_gets_the_stored_file_table_it_always_had_on_sql_server()
    {
        using var db = new StorageDbContext(Options<StorageDbContext>(SqlServer));

        var script = StoredFileStatements(db);

        Assert.Equal(Normalize(SqlServerTable), script);
        Assert.Equal(Normalize(SqlServerModel), Normalize(db.Model.FindEntityType(typeof(StoredFile))!.ToDebugString(MetadataDebugStringOptions.LongDefault)));
    }

    [Fact]
    public void A_guid_app_gets_the_stored_file_table_it_always_had_on_sqlite_and_postgres()
    {
        using var sqlite = new StorageDbContext(Options<StorageDbContext>(Sqlite));
        using var postgres = new StorageDbContext(Options<StorageDbContext>(Postgres));

        var sqliteScript = StoredFileStatements(sqlite);
        var postgresScript = StoredFileStatements(postgres);

        Assert.Equal(Normalize(SqliteTable), sqliteScript);
        Assert.Equal(Normalize(PostgresTable), postgresScript);
    }

    [Fact]
    public void An_app_with_guid_tenant_scoped_tables_keeps_the_same_stored_file_table()
    {
        using var untenanted = new ConventionsContext(Options<ConventionsContext>(SqlServer));
        using var tenanted = new GuidTenantContext(Options<GuidTenantContext>(SqlServer));

        var actual = StoredFileStatements(tenanted);

        Assert.Equal(Normalize(SqlServerTable), actual);
        Assert.Equal(Describe(untenanted), Describe(tenanted));
    }

    [Fact]
    public void An_app_that_numbers_its_tenants_keeps_an_int_tenant_column_named_TenantId()
    {
        using var db = new NumberedTenantContext(Options<NumberedTenantContext>(SqlServer));

        var script = StoredFileStatements(db);

        Assert.Contains("[TenantId] int NULL,", script, StringComparison.Ordinal);
        Assert.DoesNotContain("uniqueidentifier NULL", script, StringComparison.Ordinal);
        Assert.Empty(db.Model.FindEntityType(typeof(StoredFile))!.GetForeignKeys());
    }

    [Fact]
    public void The_int_column_is_the_same_whichever_is_called_first()
    {
        using var storageFirst = new NumberedTenantContext(Options<NumberedTenantContext>(SqlServer));
        using var conventionsFirst = new ConventionsFirstNumberedTenantContext(Options<ConventionsFirstNumberedTenantContext>(SqlServer));

        var first = StoredFileStatements(storageFirst);
        var second = StoredFileStatements(conventionsFirst);

        Assert.Equal(first, second);
        Assert.Contains("[TenantId] int NULL,", second, StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_that_numbers_its_tenants_by_long_keeps_a_bigint_column()
    {
        using var db = new LongTenantContext(Options<LongTenantContext>(SqlServer));

        var script = StoredFileStatements(db);

        Assert.Contains("[TenantId] bigint NULL,", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bytes_table_sits_beside_it_keyed_by_the_files_id()
    {
        using var sqlServer = new StorageDbContext(Options<StorageDbContext>(SqlServer));
        using var postgres = new StorageDbContext(Options<StorageDbContext>(Postgres));

        var sqlServerScript = Normalize(sqlServer.Database.GenerateCreateScript());
        var postgresScript = Normalize(postgres.Database.GenerateCreateScript());

        Assert.Contains("""
            CREATE TABLE [StoredFileBytes] (
                [Id] uniqueidentifier NOT NULL,
                [Bytes] varbinary(max) NOT NULL,
                CONSTRAINT [PK_StoredFileBytes] PRIMARY KEY ([Id])
            );
            """.ReplaceLineEndings("\n"), sqlServerScript, StringComparison.Ordinal);
        Assert.Contains("\"Bytes\" bytea NOT NULL", postgresScript, StringComparison.Ordinal);
    }

    private static readonly Action<DbContextOptionsBuilder> SqlServer = o => o.UseSqlServer("Server=.;Database=rask_shape");
    private static readonly Action<DbContextOptionsBuilder> Sqlite = o => o.UseSqlite("Data Source=:memory:");
    private static readonly Action<DbContextOptionsBuilder> Postgres = o => o.UseNpgsql("Host=localhost;Database=rask_shape");

    private static DbContextOptions<T> Options<T>(Action<DbContextOptionsBuilder> provider)
        where T : DbContext
    {
        var builder = new DbContextOptionsBuilder<T>();
        provider(builder);
        return builder.Options;
    }

    // The statements that create StoredFile and its index, as the provider scripts them — StoredFileBytes left out.
    private static string StoredFileStatements(DbContext db) =>
        string.Join("\n\n\n", Normalize(db.Database.GenerateCreateScript())
            .Split("\n\n\n")
            .Where(static block => block.Contains("StoredFile]", StringComparison.Ordinal)
                || block.Contains("StoredFile\"", StringComparison.Ordinal)));

    private static string Describe(DbContext db) =>
        Normalize(db.Model.FindEntityType(typeof(StoredFile))!.ToDebugString(MetadataDebugStringOptions.LongDefault));

    // Line endings and trailing spaces are the checkout's and the debug view's, not the model's.
    private static string Normalize(string text) =>
        string.Join("\n", text.ReplaceLineEndings("\n").Split('\n').Select(static line => line.TrimEnd())).TrimEnd('\n');
}
