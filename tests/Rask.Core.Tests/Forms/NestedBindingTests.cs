using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// Coverage for binding into sub-objects and collection items. The reference-based
// FieldIdentifier scheme means each sub-object owns its own field state — these tests
// pin that contract from the ExpressionAccessor side: parse, get, set, and re-parse must
// resolve to the right runtime instance at every depth and shape.
public class NestedBindingTests
{
    [Fact]
    public void Parse_NestedMemberChain_TargetsSubObjectInstance()
    {
        var p = new Person { Address = new Address { Street = "Elm" } };

        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => p.Address.Street));

        Assert.Same(p.Address, acc.Target);
        Assert.Equal("Street", acc.PropertyName);
        Assert.Equal(typeof(string), acc.PropertyType);
        Assert.Equal("Elm", acc.Getter());
    }

    [Fact]
    public void Parse_NestedMemberChain_SetterMutatesSubObject()
    {
        var p = new Person { Address = new Address { Street = "Elm" } };
        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => p.Address.Street));

        acc.Setter("Oak");

        Assert.Equal("Oak", p.Address.Street);
    }

    [Fact]
    public void Parse_DeepChain_ResolvesTerminalOwner()
    {
        var p = new Person
        {
            Address = new Address { Postal = new PostalInfo { Country = new Country { Code = "NL" } } }
        };

        var acc = ExpressionAccessor.Parse(
            (Expression<Func<string>>)(() => p.Address.Postal.Country.Code));

        Assert.Same(p.Address.Postal.Country, acc.Target);
        Assert.Equal("Code", acc.PropertyName);
        Assert.Equal("NL", acc.Getter());
    }

    [Fact]
    public void Parse_ForeachCapturedLocal_TargetsCurrentItem()
    {
        var items = new List<LineItem> { new() { Name = "alpha" }, new() { Name = "beta" }, new() { Name = "gamma" } };

        var captured = new List<ExpressionAccessor.Accessor>();
        foreach (var item in items)
        {
            // Per-iteration `item` capture — the lambda closes over a distinct local each
            // iteration, so each accessor must point at a different instance.
            captured.Add(ExpressionAccessor.Parse((Expression<Func<string>>)(() => item.Name)));
        }

        Assert.Equal(3, captured.Count);
        for (var i = 0; i < items.Count; i++)
        {
            Assert.Same(items[i], captured[i].Target);
            Assert.Equal(items[i].Name, captured[i].Getter());
        }
    }

    [Fact]
    public void Parse_ListIndexer_TargetsItemAtIndex()
    {
        var items = new List<LineItem> { new() { Name = "alpha" }, new() { Name = "beta" } };
        var i = 1;

        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => items[i].Name));

        Assert.Same(items[1], acc.Target);
        Assert.Equal("Name", acc.PropertyName);
        Assert.Equal("beta", acc.Getter());
    }

    [Fact]
    public void Parse_ArrayIndexer_TargetsItemAtIndex()
    {
        var items = new[] { new LineItem { Name = "alpha" }, new LineItem { Name = "beta" } };
        var i = 0;

        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => items[i].Name));

        Assert.Same(items[0], acc.Target);
        Assert.Equal("alpha", acc.Getter());
    }

    [Fact]
    public void Parse_DictionaryIndexer_TargetsValueAtKey()
    {
        var settings = new Dictionary<string, ServerConfig>
        {
            ["smtp"] = new() { Host = "smtp.example.com" },
            ["http"] = new() { Host = "api.example.com" }
        };

        var acc = ExpressionAccessor.Parse(
            (Expression<Func<string>>)(() => settings["smtp"].Host));

        Assert.Same(settings["smtp"], acc.Target);
        Assert.Equal("smtp.example.com", acc.Getter());
    }

    [Fact]
    public void Parse_RecordIndexer_ReResolvesAfterReplacement()
    {
        // Record items are immutable — the user replaces a slot rather than mutating it.
        // Calling Parse again after replacement must return an accessor whose Target is the
        // NEW record instance, not the discarded one.
        var items = new List<LineRecord> { new("alpha", 1), new("beta", 2) };
        var i = 0;

        var first = ExpressionAccessor.Parse((Expression<Func<string>>)(() => items[i].Name));
        Assert.Same(items[0], first.Target);

        items[0] = items[0] with { Name = "alphaPrime" };

        var second = ExpressionAccessor.Parse((Expression<Func<string>>)(() => items[i].Name));
        Assert.Same(items[0], second.Target);
        Assert.NotSame(first.Target, second.Target);
        Assert.Equal("alphaPrime", second.Getter());
    }

    [Fact]
    public void Parse_FieldIdentifier_KeysOnSubInstance()
    {
        // Two persons share the same Address property name but each owns a distinct instance.
        // Field identity must distinguish them so error state on one doesn't bleed into the other.
        var a = new Person { Address = new Address { Street = "Elm" } };
        var b = new Person { Address = new Address { Street = "Oak" } };

        var accA = ExpressionAccessor.Parse((Expression<Func<string>>)(() => a.Address.Street));
        var accB = ExpressionAccessor.Parse((Expression<Func<string>>)(() => b.Address.Street));

        Assert.NotEqual(accA.Field, accB.Field);
        Assert.Equal(accA.Field, accA.Field);
    }

    [Fact]
    public void Parse_SubObjectReassignedBetweenParses_NewAccessorTargetsNewInstance()
    {
        var p = new Person { Address = new Address { Street = "Elm" } };

        var before = ExpressionAccessor.Parse((Expression<Func<string>>)(() => p.Address.Street));
        var oldAddress = p.Address;

        p.Address = new Address { Street = "Maple" };

        var after = ExpressionAccessor.Parse((Expression<Func<string>>)(() => p.Address.Street));

        Assert.Same(oldAddress, before.Target);
        Assert.Same(p.Address, after.Target);
        Assert.NotSame(before.Target, after.Target);
        Assert.Equal("Maple", after.Getter());
    }

    [Fact]
    public void Parse_NullSubObject_ThrowsInvalidOperation()
    {
        var p = new Person { Address = null };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ExpressionAccessor.Parse((Expression<Func<string>>)(() => p.Address!.Street)));
        Assert.Contains("evaluated to null", ex.Message);
    }

    [Fact]
    public void Parse_WholeItemBind_ThrowsWithGuidanceMessage()
    {
        // The body is the IndexExpression itself, not a member access — bind a property of
        // the indexed item instead. The error message must point at the workaround.
        var items = new List<LineItem> { new() { Name = "alpha" } };
        var i = 0;

        var ex = Assert.Throws<ArgumentException>(() =>
            ExpressionAccessor.Parse((Expression<Func<LineItem>>)(() => items[i])));
        Assert.Contains("Items[i].SomeProperty", ex.Message);
    }

    [Fact]
    public void Parse_MethodCallBody_ThrowsWithMethodGuidance()
    {
        var p = new Person { Address = new Address { Street = "Elm" } };

        var ex = Assert.Throws<ArgumentException>(() =>
            ExpressionAccessor.Parse((Expression<Func<string>>)(() => p.GetDisplayName())));
        Assert.Contains("method", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_FieldOnObject_Throws()
    {
        // The terminal must be a property, not a field — fields can't be observed for
        // change events the way properties can.
        var holder = new FieldHolder { Value = "x" };

        Assert.Throws<ArgumentException>(() =>
            ExpressionAccessor.Parse((Expression<Func<string>>)(() => holder.Value)));
    }

    private sealed class Person
    {
        public Address? Address { get; set; }
        public string GetDisplayName() => "n/a";
    }

    private sealed class Address
    {
        public string Street { get; set; } = "";
        public PostalInfo? Postal { get; set; }
    }

    private sealed class PostalInfo
    {
        public Country? Country { get; set; }
    }

    private sealed class Country
    {
        public string Code { get; set; } = "";
    }

    private sealed class LineItem
    {
        public string Name { get; set; } = "";
    }

    private sealed record LineRecord(string Name, int Quantity);

    private sealed class ServerConfig
    {
        public string Host { get; set; } = "";
    }

    private sealed class FieldHolder
    {
        public string Value = "";
    }
}
