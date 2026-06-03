using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class ExpressionAccessorTests
{
    [Fact]
    public void Parse_StringProperty_ReturnsAccessor()
    {
        var p = new Person { Name = "Ada" };
        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => p.Name));
        Assert.Same(p, acc.Target);
        Assert.Equal("Name", acc.PropertyName);
        Assert.Equal(typeof(string), acc.PropertyType);
        Assert.Equal("Ada", acc.Getter());
        acc.Setter("Bea");
        Assert.Equal("Bea", p.Name);
    }

    [Fact]
    public void Parse_IntProperty_RoundtripsThroughObject()
    {
        var p = new Person { Age = 7 };
        var acc = ExpressionAccessor.Parse((Expression<Func<int>>)(() => p.Age));
        Assert.Equal(typeof(int), acc.PropertyType);
        Assert.Equal(7, acc.Getter());
        acc.Setter(42);
        Assert.Equal(42, p.Age);
    }

    [Fact]
    public void Parse_NonMemberExpression_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ExpressionAccessor.Parse((Expression<Func<int>>)(() => 1 + 1)));
    }

    [Fact]
    public void Parse_FieldAccess_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ExpressionAccessor.Parse((Expression<Func<int>>)(() => DummyHolder.Field)));
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }

    private static class DummyHolder
    {
        public static readonly int Field = 1;
    }
}
