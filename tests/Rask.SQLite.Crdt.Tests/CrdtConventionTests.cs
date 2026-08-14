using Microsoft.EntityFrameworkCore;

namespace Rask.SQLite.Crdt.Tests;

/// <summary>
///     The convention is the difference between an ordinary EF model and one cr-sqlite will accept, so
///     these assert the exact SQL default per CLR shape rather than merely that "a default exists".
/// </summary>
public sealed class CrdtConventionTests
{
    [Theory]
    [InlineData(nameof(Todo.Title), "''")]
    [InlineData(nameof(Todo.Attachment), "x''")]
    [InlineData(nameof(Todo.OwnerId), "'00000000-0000-0000-0000-000000000000'")]
    [InlineData(nameof(Todo.CreatedAt), "'0001-01-01 00:00:00'")]
    [InlineData(nameof(Todo.ReviewedAt), "'0001-01-01 00:00:00'")]
    [InlineData(nameof(Todo.Priority), "0")]
    [InlineData(nameof(Todo.Score), "0")]
    [InlineData(nameof(Todo.Cost), "0")]
    [InlineData(nameof(Todo.Estimate), "0")]
    [InlineData(nameof(Todo.State), "0")]
    public void Required_column_gets_a_default(string property, string expected)
    {
        Assert.Equal(expected, DefaultSqlFor<TodoContext>(property));
    }

    /// <summary>
    ///     The trap that motivates using a default <em>expression</em> rather than a value: EF drops a
    ///     default equal to the CLR default, because it cannot tell "unset" from "set to <c>false</c>".
    ///     A <c>bool</c> is where that bites, and it takes down only that one column — which reads like a
    ///     cr-sqlite bug rather than an EF one.
    /// </summary>
    [Fact]
    public void Bool_column_gets_a_default_too()
    {
        Assert.Equal("0", DefaultSqlFor<TodoContext>(nameof(Todo.Done)));
    }

    [Fact]
    public void Primary_key_is_left_alone()
    {
        // cr-sqlite identifies rows by their key; a default there would be meaningless and EF would have
        // to invent one for a value the application always supplies.
        Assert.Null(DefaultSqlFor<TodoContext>(nameof(Todo.Id)));
    }

    [Fact]
    public void Nullable_column_is_left_alone()
    {
        // A NULL is already an applicable value for a peer that has never heard of the column.
        Assert.Null(DefaultSqlFor<TodoContext>(nameof(Todo.Notes)));
    }

    [Fact]
    public void Existing_default_is_preserved()
    {
        Assert.Equal(TodoContext.ExplicitSlugDefault, DefaultSqlFor<TodoContext>(nameof(Todo.Slug)));
    }

    /// <summary>
    ///     Without the convention EF emits NOT NULL with no default for every required property — the exact
    ///     shape cr-sqlite refuses. This pins that the convention is doing real work rather than agreeing
    ///     with a default EF already applied.
    /// </summary>
    [Fact]
    public void Plain_model_has_no_defaults()
    {
        Assert.Null(DefaultSqlFor<PlainTodoContext>(nameof(Todo.Title)));
        Assert.Null(DefaultSqlFor<PlainTodoContext>(nameof(Todo.Done)));
    }

    private static string? DefaultSqlFor<TContext>(string property)
        where TContext : TodoContext
    {
        var options = new DbContextOptionsBuilder<TodoContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        return context.Model
            .FindEntityType(typeof(Todo))!
            .FindProperty(property)!
            .GetDefaultValueSql();
    }
}
