using System.Linq.Expressions;
using Rask.Core.DataGrids;

namespace Rask.Core.Tests.DataGrids;

public class DataGridKeyExtractorTests
{
    private record Row(int Id, string Name);

    [Fact]
    public void Extract_PropertyAccess_ReturnsPropertyName()
    {
        Expression<Func<Row, object?>> expr = r => r.Name;
        Assert.Equal("Name", DataGridKeyExtractor.Extract(expr));
    }

    [Fact]
    public void Extract_ValueTypeBoxing_PeelsConvert()
    {
        Expression<Func<Row, object?>> expr = r => r.Id;
        Assert.Equal("Id", DataGridKeyExtractor.Extract(expr));
    }

    [Fact]
    public void Extract_MethodCallBody_Throws()
    {
        Expression<Func<Row, object?>> expr = r => r.Name.ToUpperInvariant();
        var ex = Assert.Throws<ArgumentException>(() => DataGridKeyExtractor.Extract(expr));
        Assert.Contains("simple property access", ex.Message);
    }

    [Fact]
    public void Extract_ComputedExpression_Throws()
    {
        Expression<Func<Row, object?>> expr = r => r.Id + 1;
        Assert.Throws<ArgumentException>(() => DataGridKeyExtractor.Extract(expr));
    }
}
