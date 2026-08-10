using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.Crdt.Tests;

public sealed class CrdtOptionsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_extension_path_is_refused_by_name(string path)
    {
        var options = new RaskCrdtOptions { ExtensionPath = path };

        var error = Assert.Throws<InvalidOperationException>(() =>
            new DbContextOptionsBuilder().UseRaskCrdt(o => o.ExtensionPath = options.ExtensionPath));

        // The message has to name the file the caller is expected to supply: the failure otherwise
        // surfaces as SQLite refusing to load "", which reads like a corrupt install.
        Assert.Contains(nameof(RaskCrdtOptions.ExtensionPath), error.Message, StringComparison.Ordinal);
        Assert.Contains("crsqlite", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configured_path_is_accepted()
    {
        // Not loaded here — only validated. A path that does not exist yet is still a legitimate
        // configuration, because the file is resolved when a connection opens.
        var builder = new DbContextOptionsBuilder().UseRaskCrdt(o => o.ExtensionPath = "crsqlite.dylib");

        Assert.NotNull(builder);
    }

    [Fact]
    public void Every_table_is_promoted_when_none_are_named()
    {
        using var context = NewContext();

        Assert.Equal(["Todos"], RaskCrdtExtensions.ResolveTables(context.Model, options: null));
    }

    [Fact]
    public void Named_tables_win_over_the_model()
    {
        using var context = NewContext();
        var options = new RaskCrdtOptions { ExtensionPath = "crsqlite.dylib" };
        options.Tables.Add("Todos");

        Assert.Equal(["Todos"], RaskCrdtExtensions.ResolveTables(context.Model, options));
    }

    [Fact]
    public void A_table_named_twice_is_promoted_once()
    {
        // crsql_as_crr on an already-promoted table is an error, not a no-op, so a duplicate in the list
        // would fail the whole promotion — and duplicates arrive by accident whenever two entity types
        // share a table.
        using var context = NewContext();
        var options = new RaskCrdtOptions { ExtensionPath = "crsqlite.dylib" };
        options.Tables.Add("Todos");
        options.Tables.Add("Todos");

        Assert.Equal(["Todos"], RaskCrdtExtensions.ResolveTables(context.Model, options));
    }

    private static TodoContext NewContext() =>
        new(new DbContextOptionsBuilder<TodoContext>().UseSqlite("Data Source=:memory:").Options);
}
