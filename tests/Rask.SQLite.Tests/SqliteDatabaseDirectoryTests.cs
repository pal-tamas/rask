using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Tests;

// SQLite creates a database file but never the directories above it, so `Data Source=/tmp/rask/app.db` failed a fresh
// container's first start with "unable to open database file". The factory makes the directory when it is built.
public sealed class SqliteDatabaseDirectoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rask-sqlite-dir-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task A_database_in_a_missing_directory_opens_because_the_directory_is_created()
    {
        var file = Path.Combine(_root, "nested", "app.db");
        using var provider = new ServiceCollection()
            .AddRaskSqliteAt($"Data Source={file};Pooling=False")
            .BuildServiceProvider();

        var connection = await provider.GetRequiredService<ISqlite>().CreateOpenAsync(TestContext.Current.CancellationToken);
        await connection.DisposeAsync();

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void An_existing_directory_is_left_as_it_is()
    {
        Directory.CreateDirectory(_root);

        var created = SqliteDatabaseDirectory.EnsureFor($"Data Source={Path.Combine(_root, "app.db")}");

        Assert.Null(created);
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=shared;Mode=Memory")]
    [InlineData("Data Source=file:app.db?mode=memory")]
    [InlineData("Data Source=|DataDirectory|app.db")]
    public void A_source_that_names_no_directory_creates_none(string connectionString)
    {
        var created = SqliteDatabaseDirectory.EnsureFor(connectionString);

        Assert.Null(created);
    }

    [Fact]
    public void A_read_only_open_creates_no_directory()
    {
        var file = Path.Combine(_root, "ro", "app.db");

        var created = SqliteDatabaseDirectory.EnsureFor($"Data Source={file};Mode=ReadOnly");

        Assert.Null(created);
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }
}
