using Rask.Core.DataGrids;

namespace Rask.Core.Tests.DataGrids;

public class DataGridContextTests
{
    private record Row(int Id, string Name, decimal Total);

    private static readonly Row[] Sample =
    {
        new(1, "Bob", 30m),
        new(2, "Ada", 10m),
        new(3, "Ada", 25m),
        new(4, "Cy",  20m),
    };

    [Fact]
    public void ToggleSort_Single_AppliesAscending()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 0);
        ctx.ToggleSort("Name", r => r.Name, additive: false);

        Assert.Equal(new[] { "Ada", "Ada", "Bob", "Cy" }, ctx.VisibleRows.Select(r => r.Name));
        Assert.Single(ctx.SortRules);
        Assert.False(ctx.SortRules[0].Descending);
    }

    [Fact]
    public void ToggleSort_Cycles_AscDescNone()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 0);

        ctx.ToggleSort("Name", r => r.Name, additive: false);
        Assert.False(ctx.SortRules[0].Descending);

        ctx.ToggleSort("Name", r => r.Name, additive: false);
        Assert.True(ctx.SortRules[0].Descending);

        ctx.ToggleSort("Name", r => r.Name, additive: false);
        Assert.Empty(ctx.SortRules);
    }

    [Fact]
    public void ToggleSort_NonAdditive_ReplacesPriorRules()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 0);
        ctx.ToggleSort("Name", r => r.Name, additive: true);
        ctx.ToggleSort("Total", r => r.Total, additive: true);
        Assert.Equal(2, ctx.SortRules.Count);

        ctx.ToggleSort("Total", r => r.Total, additive: false);

        Assert.Single(ctx.SortRules);
        Assert.Equal("Total", ctx.SortRules[0].Key);
        Assert.False(ctx.SortRules[0].Descending);
    }

    [Fact]
    public void ToggleSort_Additive_AppendsThenFlipsThenRemovesInPlace()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 0);
        ctx.ToggleSort("Name", r => r.Name, additive: false);
        ctx.ToggleSort("Total", r => r.Total, additive: true);

        Assert.Equal(new[] { "Name", "Total" }, ctx.SortRules.Select(r => r.Key));
        Assert.False(ctx.SortRules[1].Descending);

        ctx.ToggleSort("Total", r => r.Total, additive: true);
        Assert.True(ctx.SortRules[1].Descending);
        Assert.Equal("Name", ctx.SortRules[0].Key);

        ctx.ToggleSort("Total", r => r.Total, additive: true);
        Assert.Single(ctx.SortRules);
        Assert.Equal("Name", ctx.SortRules[0].Key);
    }

    [Fact]
    public void VisibleRows_MultiSort_SecondaryStable()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 0);
        ctx.ToggleSort("Name", r => r.Name, additive: false);
        ctx.ToggleSort("Total", r => r.Total, additive: true);

        var rows = ctx.VisibleRows.ToArray();
        Assert.Equal(new[] { "Ada", "Ada", "Bob", "Cy" }, rows.Select(r => r.Name));
        Assert.Equal(10m, rows[0].Total);
        Assert.Equal(25m, rows[1].Total);
    }

    [Fact]
    public void VisibleRows_AppliesSortThenPage()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 2);
        ctx.ToggleSort("Name", r => r.Name, additive: false);

        Assert.Equal(new[] { "Ada", "Ada" }, ctx.VisibleRows.Select(r => r.Name));
        ctx.NextPage();
        Assert.Equal(new[] { "Bob", "Cy" }, ctx.VisibleRows.Select(r => r.Name));
    }

    [Fact]
    public void GoToPage_Clamps_BelowZero_AndAbovePageCount()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 2);

        ctx.GoToPage(-5);
        Assert.Equal(0, ctx.CurrentPage);

        ctx.GoToPage(99);
        Assert.Equal(1, ctx.CurrentPage);
    }

    [Fact]
    public void ReplaceSource_PreservesSort_ClampsPage()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 2);
        ctx.ToggleSort("Name", r => r.Name, additive: false);
        ctx.NextPage();
        Assert.Equal(1, ctx.CurrentPage);

        ctx.ReplaceSource(new[] { new Row(99, "Zed", 1m) });

        Assert.Equal(0, ctx.CurrentPage);
        Assert.Single(ctx.SortRules);
        Assert.Equal("Name", ctx.SortRules[0].Key);
        Assert.Equal(new[] { "Zed" }, ctx.VisibleRows.Select(r => r.Name));
    }

    [Fact]
    public void Changed_FiresOnce_PerMutator()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 2);
        var fires = 0;
        ctx.Changed += () => fires++;

        ctx.ToggleSort("Name", r => r.Name, additive: false);
        Assert.Equal(1, fires);

        ctx.NextPage();
        Assert.Equal(2, fires);

        ctx.PreviousPage();
        Assert.Equal(3, fires);

        ctx.ReplaceSource(Sample);
        Assert.Equal(4, fires);
    }

    [Fact]
    public void Changed_DoesNotFire_OnNoOpPageMove()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 2);
        var fires = 0;
        ctx.Changed += () => fires++;

        ctx.PreviousPage();
        Assert.Equal(0, fires);
    }

    [Fact]
    public void PageCount_EmptySource_IsOne()
    {
        var ctx = new DataGridContext<Row>(Array.Empty<Row>(), pageSize: 10);
        Assert.Equal(1, ctx.PageCount);
    }

    [Fact]
    public void PageCount_NoPaging_IsOne()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 0);
        Assert.Equal(1, ctx.PageCount);
        Assert.Equal(4, ctx.VisibleRows.Count());
    }

    [Fact]
    public void PageSize_Change_ClampsCurrentPage_AndRaisesChanged()
    {
        var ctx = new DataGridContext<Row>(Sample, pageSize: 1);
        ctx.GoToPage(3);
        Assert.Equal(3, ctx.CurrentPage);

        var fires = 0;
        ctx.Changed += () => fires++;
        ctx.PageSize = 2;

        Assert.Equal(2, ctx.PageSize);
        Assert.Equal(1, ctx.CurrentPage);
        Assert.True(fires >= 1);
    }

    [Fact]
    public void VisibleRows_NullPropertyValues_SortBeforeNonNull()
    {
        var rows = new[]
        {
            new Row(1, "Bob", 10m),
            new Row(2, null!, 20m),
            new Row(3, "Ada", 5m),
        };
        var ctx = new DataGridContext<Row>(rows, pageSize: 0);
        ctx.ToggleSort("Name", r => r.Name, additive: false);

        var names = ctx.VisibleRows.Select(r => r.Name).ToArray();
        Assert.Null(names[0]);
        Assert.Equal("Ada", names[1]);
        Assert.Equal("Bob", names[2]);
    }
}
