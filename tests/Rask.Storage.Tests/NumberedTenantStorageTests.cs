using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Storage.Tests;

/// <summary>
/// In an app whose tenants are numbered, a file records the number, and is reachable only from that tenant — the same
/// two by-id checks as a Guid app, compared as numbers.
/// </summary>
[Collection(StorageDbCollection.Name)]
public sealed class NumberedTenantStorageTests
{
    [Fact]
    public async Task A_file_records_the_tenants_number_in_an_integer_column()
    {
        await using var harness = new StorageHarness<NumberedTenantContext>();
        StoredFile file;

        using (Tenant.Use(42))
        {
            using var content = new MemoryStream(Samples.Text);
            file = await harness.Files.Save(content, "data.csv", TestContext.Current.CancellationToken);
        }

        var (type, value) = await StoredTenantAsync(harness, file.Id);
        Assert.Equal("integer", type);
        Assert.Equal(42L, value);
        using (Tenant.Use(42))
        {
            Assert.Equal(Current.Tenant, file.TenantId);
        }
    }

    [Fact]
    public async Task Another_numbered_tenant_cannot_find_read_or_delete_it()
    {
        await using var harness = new StorageHarness<NumberedTenantContext>();
        Guid id;
        using (Tenant.Use(42))
        {
            using var content = new MemoryStream(Samples.Text);
            id = (await harness.Files.Save(content, "data.csv", TestContext.Current.CancellationToken)).Id;
        }

        using (Tenant.Use(43))
        {
            Assert.Null(await harness.Files.Get(id, TestContext.Current.CancellationToken));
            Assert.Null(await harness.Files.OpenRead(id, TestContext.Current.CancellationToken));
            Assert.False(await harness.Files.Delete(id, TestContext.Current.CancellationToken));
        }

        using (Tenant.Use(42))
        {
            Assert.NotNull(await harness.Files.Get(id, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task No_tenant_reaches_only_the_files_the_host_saved()
    {
        await using var harness = new StorageHarness<NumberedTenantContext>();
        Guid owned;
        using (Tenant.Use(42))
        {
            using var content = new MemoryStream(Samples.Text);
            owned = (await harness.Files.Save(content, "owned.csv", TestContext.Current.CancellationToken)).Id;
        }

        using var hostContent = new MemoryStream(Samples.Text);
        var hosts = await harness.Files.Save(hostContent, "host.csv", TestContext.Current.CancellationToken);

        Assert.Null(hosts.TenantId);
        Assert.Null(await harness.Files.Get(owned, TestContext.Current.CancellationToken));
        Assert.NotNull(await harness.Files.Get(hosts.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_tenant_that_is_no_number_is_refused_rather_than_compared()
    {
        await using var harness = new StorageHarness<NumberedTenantContext>();
        Guid id;
        using (Tenant.Use(42))
        {
            using var content = new MemoryStream(Samples.Text);
            id = (await harness.Files.Save(content, "data.csv", TestContext.Current.CancellationToken)).Id;
        }

        using (Tenant.Use(Guid.NewGuid()))
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.Files.Get(id, TestContext.Current.CancellationToken));

            Assert.Contains("StoredFile", refused.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_guid_app_still_records_the_guid()
    {
        await using var harness = new StorageHarness<GuidTenantContext>();
        var tenant = Guid.NewGuid();
        StoredFile file;

        using (Tenant.Use(tenant))
        {
            using var content = new MemoryStream(Samples.Text);
            file = await harness.Files.Save(content, "data.csv", TestContext.Current.CancellationToken);
        }

        var (type, value) = await StoredTenantAsync(harness, file.Id);
        Assert.Equal("text", type);
        Assert.Equal(tenant.ToString().ToUpperInvariant(), value);
        using (Tenant.Use(Guid.NewGuid()))
        {
            Assert.Null(await harness.Files.Get(file.Id, TestContext.Current.CancellationToken));
        }
    }

    private static async Task<(string Type, object Value)> StoredTenantAsync<TContext>(StorageHarness<TContext> harness, Guid id)
        where TContext : DbContext
    {
        await using var connection = new SqliteConnection($"Data Source={harness.DbPath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT typeof("TenantId"), "TenantId" FROM "StoredFile" WHERE "Id" = $id""";
        command.Parameters.AddWithValue("$id", id.ToString().ToUpperInvariant());
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        return (reader.GetString(0), reader.GetValue(1));
    }
}
