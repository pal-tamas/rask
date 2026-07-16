using Rask.Core;

namespace Rask.Bootstrap.Tests;

// BsDataGrid<T> ships in a published package and real apps bind to it, so its factory surface is frozen: a
// renamed, reordered, retyped or newly-required parameter silently breaks every caller. These tests are the
// evidence for that claim rather than an intention to keep it.
//
// The call sites below mirror the shapes a consuming app actually uses (typed and inferred, array and List
// columns, with and without paging/detail/footers). They are compile-time assertions first — if the generated
// factory changes shape, this file stops building.
public class BsDataGridApiCompatTests
{
    private sealed record Supplier(Guid Id, string Name, string VatNumber);

    private static readonly List<Supplier> Suppliers = [new(Guid.NewGuid(), "Acme", "123")];

    [Fact]
    public void TypedCallSite_WithSortableAndTemplateColumns()
    {
        var html = BsDataGrid<Supplier>(
            Data: Suppliers,
            PageSize: 20,
            RowKey: s => s.Id,
            Columns:
            [
                new BsColumn<Supplier> { Title = "Name", Value = s => s.Name, Sortable = true, Class = "fw-semibold" },
                new BsColumn<Supplier> { Title = "VAT", Value = s => s.VatNumber, Sortable = true },
                new BsColumn<Supplier> { Title = "", Class = "text-end", Template = s => Span()[s.Name] },
            ]).ToHtml();

        Assert.Contains("Acme", html);
    }

    [Fact]
    public void InferredCallSite_WithAListOfColumns_AndAnEmptyPlaceholder()
    {
        // Type inference has to work from List<BsColumn<T>>, not just the declared IReadOnlyList<T>.
        List<BsColumn<Supplier>> columns =
        [
            new BsColumn<Supplier> { Title = "Name", Sortable = true, Value = s => s.Name },
        ];

        var html = BsDataGrid(Data: Suppliers, Columns: columns, RowKey: s => s.Id, PageSize: 50, Small: true,
            Empty: Div()["Nothing found."]).ToHtml();

        Assert.Contains("table-sm", html);
    }

    [Fact]
    public void CallSite_WithExpandedContentAndAFooterTemplate()
    {
        List<BsColumn<Supplier>> columns =
        [
            new BsColumn<Supplier>
            {
                Title = "Name", Class = "text-end", Value = s => s.Name,
                FooterTemplate = rows => Span(Class: "fw-bold")[rows.Count.ToString()],
            },
        ];

        var html = BsDataGrid(Data: Suppliers, Columns: columns, RowKey: s => s.Id, PageSize: 50, Small: true,
            Empty: Div()["Nothing."], ExpandedContent: s => Div()[s.Name]).ToHtml();

        Assert.Contains("<tfoot>", html);
    }

    [Fact]
    public void PositionalCallSite_StillBinds()
    {
        // Guards parameter ORDER, which named arguments would hide. Data, Columns, PageSize are the first three.
        var html = BsDataGrid<Supplier>(Suppliers, [new BsColumn<Supplier> { Title = "N", Value = s => s.Name }], 10)
            .ToHtml();

        Assert.Contains("Acme", html);
    }

    [Fact]
    public void EveryParameter_IsOptional()
    {
        // A required parameter would be a breaking change: a non-nullable property with no initializer becomes
        // one (RASK001), which is why PageSize keeps its `= 0`.
        Assert.NotNull(BsDataGrid<Supplier>().ToHtml());
    }

    [Fact]
    public void BsColumn_PublicSurface_IsFrozen()
    {
        var actual = typeof(BsColumn<>).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(
            [
                "Class", "Field", "Footer", "FooterTemplate", "GroupHeader", "GroupKey", "Groupable",
                "RowClickable", "SortBy", "SortField", "SortKey", "Sortable", "Template", "Title", "Value",
            ],
            actual);
    }

    [Fact]
    public void NewParameters_AreAppended_SoPositionalCallersDoNotShift()
    {
        // The generator orders same-depth parameters by declaration order, and hoists REQUIRED ones (a
        // non-nullable property with no initializer, RASK001) ahead of everything. Either mistake silently
        // re-binds existing positional arguments. Pin the original ten, in order, at the front.
        var factory = typeof(Rask.Bootstrap.Generated)
            .GetMethods()
            .Single(m => m.Name == "BsDataGrid" && m.IsGenericMethodDefinition);

        Assert.Equal(
            [
                "Data", "Columns", "PageSize", "Striped", "Hover", "Small", "Responsive", "RowKey", "Empty",
                "ExpandedContent",
            ],
            factory.GetParameters().Take(10).Select(p => p.Name));

        // ...and every one of them stays optional.
        Assert.All(factory.GetParameters(), p => Assert.True(p.IsOptional, $"{p.Name} must stay optional"));

        // Data takes IEnumerable<T> so an IQueryable (a DbSet) binds to it directly. Narrowing this back to
        // IReadOnlyList<T> would silently stop every store-side grid from compiling.
        Assert.StartsWith("IEnumerable`1", factory.GetParameters()[0].ParameterType.Name);
    }

    [Fact]
    public void BsDataGrid_PublicSurface_IsFrozen()
    {
        // Id/Class arrive from BsBlock — the passthrough every other Bs component already had. Anything else
        // appearing here is an unintended addition to the public factory. Indexers are the children syntax,
        // not properties the generator turns into parameters.
        var actual = typeof(BsDataGrid<>).GetProperties()
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(
            [
                "Children", "Class", "Columns", "Data", "Empty", "ExpandedContent", "GroupCollapsible",
                "GroupPanel", "GroupSubtotals", "Grouped", "Hover", "Id", "Key", "Loading", "MaxHeight",
                "OnGroupedChange",
                "OnGroupedChangeAsync", "OnPageChange", "OnPageChangeAsync", "OnRowClick", "OnRowClickAsync",
                "OnSelectionChange", "OnSelectionChangeAsync", "OnSortChange", "OnSortChangeAsync", "Page",
                "PageSize", "Responsive", "RowClass", "RowKey", "Selectable", "SelectedKeys", "Small", "Sort",
                "SortDescending", "StickyHeader", "Striped", "TotalCount",
            ],
            actual);
    }
}
