using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>
///     Which connections <c>Db.Transaction</c> puts back to the default isolation once its transaction ends. Only SQL
///     Server keeps a level past the transaction — the provider gate proves the reset there — so nothing else pays the
///     round trip.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class IsolationResetTests
{
    [Theory]
    [InlineData(IsolationLevel.Serializable)]
    [InlineData(IsolationLevel.ReadUncommitted)]
    public void A_sqlite_connection_needs_no_reset_whatever_level_the_block_asked_for(IsolationLevel level)
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options);

        var reset = IsolationReset.After(context, level);

        Assert.Null(reset);
    }

    [Fact]
    public void A_block_that_asked_for_no_level_needs_no_reset()
    {
        using var context = new DbContext(new DbContextOptionsBuilder().UseSqlite("Data Source=:memory:").Options);

        var reset = IsolationReset.After(context, isolation: null);

        Assert.Null(reset);
    }
}
