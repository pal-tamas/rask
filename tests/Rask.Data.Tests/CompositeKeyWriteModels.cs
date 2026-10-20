namespace Rask.Data.Tests.Reports;

/// <summary>A report of an existing app: numbered tenants, links to what it covers and a layout of columns.</summary>
public sealed class Report : Aggregate<int, PerTenant<int>>
{
    private readonly List<ReportSite> _sites = [];
    private readonly List<ReportColumn> _layout = [];

    public string Title { get; private set; } = "";

    public IReadOnlyList<ReportSite> Sites => _sites;

    public IReadOnlyList<ReportColumn> Layout => _layout;

    public static Report Titled(string title, IEnumerable<int> siteIds, IEnumerable<(int ColumnId, int Order, bool Sum, int[] Totals)> layout)
    {
        var report = new Report { Title = title };
        report.Cover(siteIds);
        report.Lay(layout);
        return report;
    }

    public void Cover(IEnumerable<int> siteIds)
    {
        _sites.Clear();
        _sites.AddRange(siteIds.Select(ReportSite.For));
    }

    public void Lay(IEnumerable<(int ColumnId, int Order, bool Sum, int[] Totals)> layout)
    {
        _layout.Clear();
        _layout.AddRange(layout.Select(static c => ReportColumn.For(c.ColumnId, c.Order, c.Sum, c.Totals)));
    }
}

/// <summary>A site a report can cover.</summary>
public sealed class Site : Aggregate<int, PerTenant<int>>
{
    public string Name { get; private set; } = "";
}

/// <summary>Which sites a report covers: a link keyed by the pair.</summary>
public sealed partial class ReportSite : Entity<(Report, Site)>
{
    internal static ReportSite For(int siteId) => new() { SiteId = siteId };
}

/// <summary>A column of a report's layout, keyed by the report and the column it shows.</summary>
public sealed partial class ReportColumn : Entity<(Report, int ColumnId)>
{
    private readonly List<ReportGroupTotal> _totals = [];

    public int ColumnId { get; private set; }

    public int Order { get; private set; }

    public bool Sum { get; private set; }

    public IReadOnlyList<ReportGroupTotal> Totals => _totals;

    internal static ReportColumn For(int columnId, int order, bool sum, IEnumerable<int> totals)
    {
        var column = new ReportColumn { ColumnId = columnId, Order = order, Sum = sum };
        column._totals.AddRange(totals.Select(ReportGroupTotal.For));
        return column;
    }
}

/// <summary>A total a column's group shows: a grandchild, keyed under the column that holds it.</summary>
public sealed partial class ReportGroupTotal : Entity<(ReportColumn, int TotalColumnId)>
{
    public int TotalColumnId { get; private set; }

    internal static ReportGroupTotal For(int totalColumnId) => new() { TotalColumnId = totalColumnId };
}
