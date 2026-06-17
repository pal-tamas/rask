using Rask.Core.Tables;

#pragma warning disable RASK014 // test-defined StubComponent has no generated factory

namespace Rask.Core.Tests.Components;

public class TableModelTests
{
    private static readonly Person[] People =
    [
        new(1, "Ada", "London"),
        new(2, "Linus", "Helsinki"),
        new(3, "Grace", "New York")
    ];

    private static IReadOnlyList<ColumnDef<Person>> Columns() =>
    [
        new() { Id = "name", Header = "Name", Value = p => p.Name },
        new() { Id = "city", Value = p => p.City }, // Header omitted → defaults to "city"
        new() { Id = "id", Sortable = false, Value = p => p.Id }
    ];

    [Fact]
    public void Render_HeadlessNoOwnDom_OnlyEmitsUserMarkup()
    {
        var view = new StubComponent(() => TableModel<Person>(
            ctx => Div()["x"],
            Columns(),
            People));

        Assert.Equal("<div>x</div>", view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_NullColumns_Throws()
    {
        var view = new StubComponent(() => TableModel<Person>(ctx => Div(), null!, People));
        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_EmptyColumns_Throws()
    {
        var view = new StubComponent(() => TableModel<Person>(
            ctx => Div(), Array.Empty<ColumnDef<Person>>(), People));
        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_NullRows_Throws()
    {
        var view = new StubComponent(() => TableModel<Person>(ctx => Div(), Columns(), null!));
        Assert.Throws<InvalidOperationException>(() => view.RenderAsLiveRoot());
    }

    [Fact]
    public void Render_EmptyRows_IsValid()
    {
        TableModelContext<Person>? captured = null;
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div()["empty"];
            },
            Columns(),
            Array.Empty<Person>()));

        Assert.Equal("<div>empty</div>", view.RenderAsLiveRoot());
        Assert.Empty(captured!.Rows);
        Assert.False(captured.AllSelected);
    }

    [Fact]
    public void Headers_ReflectColumnDefs_InOrder_WithDefaultsAndSortState()
    {
        TableModelContext<Person>? captured = null;
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            Sort: [new ColumnSort("name", SortDirection.Descending)]));

        view.RenderAsLiveRoot();
        var headers = captured!.Headers;

        Assert.Equal(["name", "city", "id"], headers.Select(h => h.ColumnId));
        Assert.Equal("Name", headers[0].Header);
        Assert.Equal("city", headers[1].Header); // defaulted from Id
        Assert.Equal(SortDirection.Descending, headers[0].Direction);
        Assert.Equal(0, headers[0].SortPriority);
        Assert.Null(headers[1].Direction);
        Assert.Equal(-1, headers[1].SortPriority);
        Assert.False(headers[2].Sortable);
        Assert.Null(headers[2].Direction);
    }

    [Theory]
    [InlineData(null, SortDirection.Ascending)] // unsorted → asc
    [InlineData(SortDirection.Ascending, SortDirection.Descending)] // asc → desc
    public void ToggleSort_Single_CyclesAscThenDesc(SortDirection? current, SortDirection expected)
    {
        var sort = current is { } c ? new[] { new ColumnSort("name", c) } : Array.Empty<ColumnSort>();
        var (ctx, proposals) = RenderCapturingSort(sort, multiSort: false);

        ctx.ToggleSort("name");

        var proposed = Assert.Single(proposals);
        var entry = Assert.Single(proposed);
        Assert.Equal("name", entry.ColumnId);
        Assert.Equal(expected, entry.Direction);
        // Controlled: the table's own view still reflects the OLD Sort prop — no internal latch.
        Assert.Equal(sort.Length, ctx.Sort.Count);
    }

    [Fact]
    public void ToggleSort_Single_DescCyclesToNone()
    {
        var (ctx, proposals) = RenderCapturingSort(
            [new ColumnSort("name", SortDirection.Descending)], multiSort: false);

        ctx.ToggleSort("name");

        Assert.Empty(Assert.Single(proposals));
    }

    [Fact]
    public void ToggleSort_NonSortableColumn_DoesNothing()
    {
        var (ctx, proposals) = RenderCapturingSort(Array.Empty<ColumnSort>(), multiSort: false);
        ctx.ToggleSort("id"); // id column is Sortable = false
        Assert.Empty(proposals);
    }

    [Fact]
    public void ToggleSort_Multi_AppendsUpdatesAndRemoves_PreservingOthers()
    {
        // append a second column
        var (ctx1, p1) = RenderCapturingSort([new ColumnSort("name", SortDirection.Ascending)], multiSort: true);
        ctx1.ToggleSort("city");
        Assert.Equal(
            [new ColumnSort("name", SortDirection.Ascending), new ColumnSort("city", SortDirection.Ascending)],
            Assert.Single(p1));

        // update asc → desc, preserving the other entry's order
        var (ctx2, p2) = RenderCapturingSort(
            [new ColumnSort("name", SortDirection.Ascending), new ColumnSort("city", SortDirection.Ascending)],
            multiSort: true);
        ctx2.ToggleSort("name");
        Assert.Equal(
            [new ColumnSort("name", SortDirection.Descending), new ColumnSort("city", SortDirection.Ascending)],
            Assert.Single(p2));

        // desc → removed, leaving the rest
        var (ctx3, p3) = RenderCapturingSort(
            [new ColumnSort("name", SortDirection.Descending), new ColumnSort("city", SortDirection.Ascending)],
            multiSort: true);
        ctx3.ToggleSort("name");
        Assert.Equal([new ColumnSort("city", SortDirection.Ascending)], Assert.Single(p3));
    }

    [Fact]
    public void Headers_SortPriority_ReflectsMultiColumnOrder()
    {
        TableModelContext<Person>? captured = null;
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            Sort: [new ColumnSort("city", SortDirection.Ascending), new ColumnSort("name", SortDirection.Ascending)]));

        view.RenderAsLiveRoot();
        Assert.Equal(0, captured!.Headers.Single(h => h.ColumnId == "city").SortPriority);
        Assert.Equal(1, captured.Headers.Single(h => h.ColumnId == "name").SortPriority);
    }

    [Fact]
    public void ClearSort_ProposesEmptySort()
    {
        var (ctx, proposals) = RenderCapturingSort(
            [new ColumnSort("name", SortDirection.Ascending)], multiSort: false);
        ctx.ClearSort();
        Assert.Empty(Assert.Single(proposals));
    }

    [Fact]
    public void Rows_Projection_ExposesValueKeyIndexAndSelection()
    {
        TableModelContext<Person>? captured = null;
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            KeySelector: p => p.Id,
            SelectedKeys: [2]));

        view.RenderAsLiveRoot();
        var rows = captured!.Rows;

        Assert.Equal(3, rows.Count);
        Assert.Equal(0, rows[0].RowIndex);
        Assert.Equal("Ada", rows[0].Value.Name);
        Assert.Equal(1, rows[0].Key);
        Assert.False(rows[0].IsSelected);
        Assert.True(rows[1].IsSelected); // id 2
        Assert.True(captured.IsSelected(People[1]));
        Assert.False(captured.IsSelected(People[0]));
    }

    [Fact]
    public void ToggleRow_ByKeySelector_AddsAndRemoves()
    {
        var (ctx, proposals) = RenderCapturingSelection([2], keyBySelector: true);

        ctx.ToggleRow(People[0]); // id 1 not selected → add
        var added = Assert.Single(proposals);
        Assert.Contains((object)1, added);
        Assert.Contains((object)2, added);

        proposals.Clear();
        ctx.ToggleRow(People[1]); // id 2 selected → remove
        Assert.DoesNotContain((object)2, Assert.Single(proposals));
    }

    [Fact]
    public void ToggleRow_ByReference_WhenNoKeySelector()
    {
        TableModelContext<Person>? captured = null;
        var proposals = new List<IReadOnlyCollection<object>>();
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            SelectedKeys: [People[1]], // identity is the row reference
            OnSelect: s => proposals.Add(s)));

        view.RenderAsLiveRoot();
        Assert.True(captured!.Rows[1].IsSelected);

        captured.ToggleRow(People[1]); // remove the referenced row
        Assert.DoesNotContain(People[1], Assert.Single(proposals));
    }

    [Fact]
    public void ToggleAll_SelectsAllOrClears_AndAllSelectedFlag()
    {
        var (ctxNone, pNone) = RenderCapturingSelection([], keyBySelector: true);
        Assert.False(ctxNone.AllSelected);
        ctxNone.ToggleAll();
        var all = Assert.Single(pNone);
        Assert.Equal(new object[] { 1, 2, 3 }.OrderBy(x => x), all.OrderBy(x => x));

        var (ctxAll, pAll) = RenderCapturingSelection([1, 2, 3], keyBySelector: true);
        Assert.True(ctxAll.AllSelected);
        ctxAll.ToggleAll();
        Assert.Empty(Assert.Single(pAll));
    }

    [Fact]
    public void Pagination_EchoesState_AndClampsAndFiresOnlyOnChange()
    {
        TableModelContext<Person>? captured = null;
        var pages = new List<int>();
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            PageIndex: 1,
            PageCount: 3,
            PageSize: 10,
            TotalRowCount: 25,
            OnPage: p => pages.Add(p)));

        view.RenderAsLiveRoot();
        Assert.Equal(1, captured!.PageIndex);
        Assert.Equal(3, captured.PageCount);
        Assert.Equal(10, captured.PageSize);
        Assert.Equal(25, captured.TotalRowCount);
        Assert.True(captured.HasPrevPage);
        Assert.True(captured.HasNextPage);

        captured.NextPage();
        Assert.Equal(2, pages[^1]);
        captured.PrevPage();
        Assert.Equal(0, pages[^1]);
        captured.SetPage(99); // clamps to PageCount-1 = 2
        Assert.Equal(2, pages[^1]);

        var before = pages.Count;
        captured.SetPage(1); // equals current PageIndex → no event
        Assert.Equal(before, pages.Count);
    }

    [Fact]
    public void ControlledLoop_HostAppliesSort_TableReflectsNewProp()
    {
        IReadOnlyList<ColumnSort> sort = Array.Empty<ColumnSort>();
        TableModelContext<Person>? captured = null;
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            Sort: sort,
            OnSort: s => sort = s)); // host owns + applies the state

        view.RenderAsLiveRoot();
        Assert.Empty(captured!.Sort);

        captured.ToggleSort("name");
        Assert.Single(sort); // host applied the proposal

        view.RenderAsLiveRoot(); // new Sort prop flows back in
        Assert.Single(captured!.Sort);
        Assert.Equal("name", captured.Sort[0].ColumnId);
        Assert.Equal(SortDirection.Ascending, captured.Sort[0].Direction);
    }

    [Fact]
    public void NullCallbacks_HandlersAreSafeNoOps()
    {
        TableModelContext<Person>? captured = null;
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            PageCount: 3));

        view.RenderAsLiveRoot();

        // No OnSort/OnPage/OnSelect supplied — every action must no-op rather than throw.
        captured!.ToggleSort("name");
        captured.ClearSort();
        captured.SetPage(2);
        captured.NextPage();
        captured.ToggleRow(People[0]);
        captured.ToggleAll();
    }

    [Fact]
    public void OnSortAsync_IsInvoked_WhenSyncCallbackAbsent()
    {
        TableModelContext<Person>? captured = null;
        IReadOnlyList<ColumnSort>? proposed = null;
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            OnSortAsync: s =>
            {
                proposed = s;
                return Task.CompletedTask;
            }));

        view.RenderAsLiveRoot();
        captured!.ToggleSort("name");

        Assert.NotNull(proposed);
        Assert.Equal("name", proposed![0].ColumnId);
    }

    [Fact]
    public void TwoTableModels_DifferentRowTypes_CoexistInSameTree()
    {
        TableModelContext<Person>? a = null;
        TableModelContext<int>? b = null;
        IReadOnlyList<ColumnDef<int>> intCols = [new() { Id = "v", Value = x => x }];

        var view = new StubComponent(() => Div()[
            TableModel<Person>(
                ctx =>
                {
                    a = ctx;
                    return Span()["p"];
                },
                Columns(),
                People),
            TableModel<int>(
                ctx =>
                {
                    b = ctx;
                    return Span()["i"];
                },
                intCols,
                [10, 20])
        ]);

        var html = view.RenderAsLiveRoot();

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(3, a!.Rows.Count);
        Assert.Equal(2, b!.Rows.Count);
        Assert.Equal(20, b.Rows[1].Value);
        Assert.Contains("<span>p</span>", html);
        Assert.Contains("<span>i</span>", html);
    }

    private static (TableModelContext<Person> ctx, List<IReadOnlyList<ColumnSort>> proposals) RenderCapturingSort(
        IReadOnlyList<ColumnSort> sort, bool multiSort)
    {
        TableModelContext<Person>? captured = null;
        var proposals = new List<IReadOnlyList<ColumnSort>>();
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            Sort: sort,
            MultiSort: multiSort,
            OnSort: s => proposals.Add(s)));

        view.RenderAsLiveRoot();
        return (captured!, proposals);
    }

    private static (TableModelContext<Person> ctx, List<IReadOnlyCollection<object>> proposals) RenderCapturingSelection(
        IReadOnlyCollection<object> selected, bool keyBySelector)
    {
        TableModelContext<Person>? captured = null;
        var proposals = new List<IReadOnlyCollection<object>>();
        var view = new StubComponent(() => TableModel<Person>(
            ctx =>
            {
                captured = ctx;
                return Div();
            },
            Columns(),
            People,
            KeySelector: keyBySelector ? p => p.Id : null,
            SelectedKeys: selected,
            OnSelect: s => proposals.Add(s)));

        view.RenderAsLiveRoot();
        return (captured!, proposals);
    }

    private sealed record Person(int Id, string Name, string City);
}
