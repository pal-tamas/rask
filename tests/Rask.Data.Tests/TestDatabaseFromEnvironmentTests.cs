using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

[Collection(DataDbCollection.Name)]
public sealed class TestDatabaseFromEnvironmentTests : IDisposable
{
    private readonly string _variable = $"RASK_TEST_DB_{Guid.NewGuid():N}";
    private readonly string _directory = Directory.CreateTempSubdirectory("rask-env-db-").FullName;

    private string NamedPath => Path.Combine(_directory, "shop.db");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_variable, null);
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void An_unset_variable_skips_the_test_rather_than_failing_it()
    {
        var unset = Environment.GetEnvironmentVariable(_variable) ?? "";

        var skip = TestDatabase.SkipWithout(_variable, unset);

        Assert.NotNull(skip);
        Assert.StartsWith("$XunitDynamicSkip$", skip.Message, StringComparison.Ordinal);
        Assert.Contains($"{_variable} is not set", skip.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_set_variable_is_not_skipped()
    {
        var skip = TestDatabase.SkipWithout(_variable, "Data Source=shop.db");

        Assert.Null(skip);
    }

    [Fact]
    public async Task A_set_variable_hands_out_a_factory_over_a_database_of_the_tests_own()
    {
        Environment.SetEnvironmentVariable(_variable, $"Data Source={NamedPath}");
        await using var database = await OpenAsync();

        await using (var writing = await database.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            writing.Notes.Add(new LedgerNote { Text = "opened" });
            await writing.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = await database.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["opened"], await reading.Notes.Select(n => n.Text).ToListAsync(TestContext.Current.CancellationToken));
        Assert.False(File.Exists(NamedPath));
        Assert.Single(Directory.GetFiles(_directory, "shop-*.db"));
    }

    [Fact]
    public async Task A_database_of_the_tests_own_is_dropped_when_the_test_ends()
    {
        Environment.SetEnvironmentVariable(_variable, $"Data Source={NamedPath}");
        var database = await OpenAsync();
        await GeneratedModelWrites.Create(Widget.Create("anvil"), cancellationToken: TestContext.Current.CancellationToken);

        await database.DisposeAsync();

        Assert.Empty(Directory.GetFiles(_directory, "shop-*.db"));
        Assert.False(Db.IsOn);
    }

    [Fact]
    public async Task A_shared_database_keeps_what_the_test_before_wrote()
    {
        Environment.SetEnvironmentVariable(_variable, $"Data Source={NamedPath}");
        await using (var first = await OpenAsync(o => o.Shared = true))
        {
            first.Context.Notes.Add(new LedgerNote { Text = "left behind" });
            await first.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var second = await OpenAsync(o => o.Shared = true);

        Assert.Equal(["left behind"], await second.Context.Notes.Select(n => n.Text).ToListAsync(TestContext.Current.CancellationToken));
        Assert.True(File.Exists(NamedPath));
    }

    [Fact]
    public async Task A_shared_database_waits_while_another_run_holds_its_lock()
    {
        Environment.SetEnvironmentVariable(_variable, $"Data Source={NamedPath}");
        var lockFile = Path.Combine(_directory, "shop.lock");
        var otherRun = new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var opening = OpenAsync(o => (o.Shared, o.LockFile) = (true, lockFile));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var waited = !opening.IsCompleted;
        await otherRun.DisposeAsync();
        await using var database = await opening;

        Assert.True(waited);
        Assert.Empty(await database.Context.Notes.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_second_database_in_one_process_waits_for_the_first_to_be_disposed()
    {
        Environment.SetEnvironmentVariable(_variable, $"Data Source={NamedPath}");
        var first = await OpenAsync();

        var opening = OpenAsync();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var waited = !opening.IsCompleted;
        await first.DisposeAsync();
        await using var second = await opening;

        Assert.True(waited);
        Assert.True(Db.IsOn);
    }

    [Theory]
    [InlineData("Server=db;Initial Catalog=Shop;User ID=sa", "Initial Catalog=Shop_t1")]
    [InlineData("Host=db;Database=shop", "Database=shop_t1")]
    [InlineData("Server=db;Integrated Security=true", "Database=rask_test_t1")]
    public void A_server_database_of_the_tests_own_is_named_after_the_one_the_variable_names(string named, string expected)
    {
        var own = PerTestConnectionString.For(named, "Microsoft.EntityFrameworkCore.SqlServer", "t1");

        Assert.Contains(expected, own, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_in_memory_sqlite_database_is_already_the_tests_own()
    {
        var own = PerTestConnectionString.For("Data Source=:memory:", "Microsoft.EntityFrameworkCore.Sqlite", "t1");

        Assert.Equal("data source=:memory:", own, ignoreCase: true);
    }

    private Task<TestDatabase<LedgerContext>> OpenAsync(Action<TestDatabaseOptions>? options = null) =>
        TestDatabase.FromEnvironment<LedgerContext>(_variable, (o, cs) => o.UseSqlite(cs), options, TestContext.Current.CancellationToken);
}
