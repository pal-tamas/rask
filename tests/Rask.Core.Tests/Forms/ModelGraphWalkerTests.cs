using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

public class ModelGraphWalkerTests
{
    [Fact]
    public void Walk_NullRoot_YieldsNothing() => Assert.Empty(ModelGraphWalker.Walk(null!));

    [Fact]
    public void Walk_LeafOnlyRoot_YieldsRootOnly()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.Single(nodes);
        Assert.Same(p, nodes[0]);
    }

    [Fact]
    public void Walk_NestedSubObject_YieldsRootAndSub()
    {
        var p = new Person { Address = new Address { Street = "Elm" } };
        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.Contains(p, nodes);
        Assert.Contains(p.Address!, nodes);
    }

    [Fact]
    public void Walk_NullSubProperty_SkipsCleanly()
    {
        var p = new Person { Address = null };
        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.Single(nodes);
        Assert.Same(p, nodes[0]);
    }

    [Fact]
    public void Walk_DeepChain_YieldsEveryLevel()
    {
        var p = new Person
        {
            Address = new Address
            {
                Street = "Elm",
                Postal = new PostalInfo { Country = new Country { Code = "NL" } }
            }
        };

        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.Contains(p, nodes);
        Assert.Contains(p.Address!, nodes);
        Assert.Contains(p.Address!.Postal!, nodes);
        Assert.Contains(p.Address!.Postal!.Country!, nodes);
    }

    [Fact]
    public void Walk_ListItems_AreEnumerated()
    {
        var p = new Person { Items = new List<LineItem> { new() { Name = "alpha" }, new() { Name = "beta" } } };

        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.Contains(p.Items![0], nodes);
        Assert.Contains(p.Items![1], nodes);
    }

    [Fact]
    public void Walk_ListContainingNulls_SkipsNullsButContinues()
    {
        var p = new Person { Items = new List<LineItem> { new() { Name = "alpha" }, null!, new() { Name = "gamma" } } };

        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.Contains(p.Items![0]!, nodes);
        Assert.Contains(p.Items![2]!, nodes);
        Assert.DoesNotContain(null!, nodes);
    }

    [Fact]
    public void Walk_DictionaryValues_AreWalked_KeysIgnored()
    {
        var p = new Person
        {
            Settings = new Dictionary<string, ServerConfig>
            {
                ["smtp"] = new() { Host = "smtp.example.com" },
                ["http"] = new() { Host = "api.example.com" }
            }
        };

        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.Contains(p.Settings!["smtp"], nodes);
        Assert.Contains(p.Settings!["http"], nodes);
        // Keys are strings (leaves) and would be skipped anyway, but assert we didn't
        // double-yield them as nodes.
        Assert.DoesNotContain("smtp", nodes);
    }

    [Fact]
    public void Walk_Cycle_DoesNotLoop()
    {
        var a = new Cyclic { Name = "a" };
        var b = new Cyclic { Name = "b" };
        a.Next = b;
        b.Next = a;

        var nodes = ModelGraphWalker.Walk(a).Take(10).ToList();
        Assert.Equal(2, nodes.Count);
        Assert.Contains(a, nodes);
        Assert.Contains(b, nodes);
    }

    [Fact]
    public void Walk_SelfReference_YieldsOnce()
    {
        var a = new Cyclic { Name = "a" };
        a.Next = a;

        var nodes = ModelGraphWalker.Walk(a).Take(10).ToList();
        Assert.Single(nodes);
        Assert.Same(a, nodes[0]);
    }

    [Fact]
    public void Walk_StringsAndPrimitives_AreLeaves()
    {
        var p = new Person { Name = "Ada", Age = 30 };
        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.DoesNotContain("Ada", nodes);
        // Boxed ints aren't yielded — they're leaf primitives. (And they wouldn't survive
        // GetProperty boxing anyway, but the leaf filter is the relevant invariant.)
        Assert.DoesNotContain(30, nodes);
    }

    [Fact]
    public void Walk_PropertyGetterThatThrows_IsSwallowed()
    {
        var p = new ThrowyHolder();
        // Should not propagate the property getter's exception.
        var nodes = ModelGraphWalker.Walk(p).ToList();
        Assert.Single(nodes);
        Assert.Same(p, nodes[0]);
    }

    [Fact]
    public void Resolve_NullRoot_ReturnsNull() =>
        Assert.Null(ModelGraphWalker.Resolve(null!, "Anything"));

    [Fact]
    public void Resolve_EmptyPath_ReturnsNull() =>
        Assert.Null(ModelGraphWalker.Resolve(new Person(), ""));

    [Fact]
    public void Resolve_SimpleProperty_ReturnsRootAndName()
    {
        var p = new Person { Name = "Ada" };
        var r = ModelGraphWalker.Resolve(p, "Name");
        Assert.NotNull(r);
        Assert.Same(p, r!.Value.Owner);
        Assert.Equal("Name", r.Value.Property);
    }

    [Fact]
    public void Resolve_NestedPath_TargetsSubInstance()
    {
        var p = new Person { Address = new Address { Street = "Elm" } };
        var r = ModelGraphWalker.Resolve(p, "Address.Street");
        Assert.NotNull(r);
        Assert.Same(p.Address, r!.Value.Owner);
        Assert.Equal("Street", r.Value.Property);
    }

    [Fact]
    public void Resolve_IndexerPath_TargetsListItemProperty()
    {
        var p = new Person { Items = new List<LineItem> { new() { Name = "alpha" }, new() { Name = "beta" } } };

        var r = ModelGraphWalker.Resolve(p, "Items[1].Name");
        Assert.NotNull(r);
        Assert.Same(p.Items![1], r!.Value.Owner);
        Assert.Equal("Name", r.Value.Property);
    }

    [Fact]
    public void Resolve_ArrayIndexerPath_TargetsArrayItemProperty()
    {
        var p = new Person { ItemsArray = new[] { new LineItem { Name = "alpha" }, new LineItem { Name = "beta" } } };

        var r = ModelGraphWalker.Resolve(p, "ItemsArray[0].Name");
        Assert.NotNull(r);
        Assert.Same(p.ItemsArray![0], r!.Value.Owner);
        Assert.Equal("Name", r.Value.Property);
    }

    [Fact]
    public void Resolve_DictionaryIndexerPath_TargetsValueProperty()
    {
        var p = new Person
        {
            Settings = new Dictionary<string, ServerConfig> { ["smtp"] = new() { Host = "smtp.example.com" } }
        };

        // FluentValidation's standard format for collection-indexed paths uses [N];
        // dictionaries we expose accept either "key" (with quotes) or the bare key.
        var r = ModelGraphWalker.Resolve(p, "Settings[\"smtp\"].Host");
        Assert.NotNull(r);
        Assert.Same(p.Settings!["smtp"], r!.Value.Owner);
        Assert.Equal("Host", r.Value.Property);
    }

    [Fact]
    public void Resolve_DeepIndexerChain_TargetsTerminal()
    {
        var p = new Person { Items = new List<LineItem> { new() { Vendor = new Vendor { Name = "Acme" } } } };

        var r = ModelGraphWalker.Resolve(p, "Items[0].Vendor.Name");
        Assert.NotNull(r);
        Assert.Same(p.Items![0].Vendor, r!.Value.Owner);
        Assert.Equal("Name", r.Value.Property);
    }

    [Fact]
    public void Resolve_OutOfRangeIndex_ReturnsNull()
    {
        var p = new Person { Items = new List<LineItem> { new() { Name = "alpha" } } };
        Assert.Null(ModelGraphWalker.Resolve(p, "Items[7].Name"));
    }

    [Fact]
    public void Resolve_MissingProperty_ReturnsNull()
    {
        var p = new Person { Address = new Address { Street = "Elm" } };
        Assert.Null(ModelGraphWalker.Resolve(p, "Address.Nope"));
    }

    [Fact]
    public void Resolve_NullIntermediate_ReturnsNull()
    {
        var p = new Person { Address = null };
        Assert.Null(ModelGraphWalker.Resolve(p, "Address.Street"));
    }

    [Fact]
    public void Resolve_BareCollectionItem_ReturnsNull()
    {
        // No terminal property — Items[0] alone has nothing to register against.
        var p = new Person { Items = new List<LineItem> { new() { Name = "alpha" } } };
        Assert.Null(ModelGraphWalker.Resolve(p, "Items[0]"));
    }

    [Fact]
    public void Resolve_UnterminatedBracket_ReturnsNull()
    {
        var p = new Person { Items = new List<LineItem> { new() { Name = "alpha" } } };
        Assert.Null(ModelGraphWalker.Resolve(p, "Items[0.Name"));
    }

    private sealed class Person
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public Address? Address { get; set; }
        public List<LineItem>? Items { get; set; }
        public LineItem[]? ItemsArray { get; set; }
        public Dictionary<string, ServerConfig>? Settings { get; set; }
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
        public Vendor? Vendor { get; set; }
    }

    private sealed class Vendor
    {
        public string Name { get; set; } = "";
    }

    private sealed class ServerConfig
    {
        public string Host { get; set; } = "";
    }

    private sealed class Cyclic
    {
        public string Name { get; set; } = "";
        public Cyclic? Next { get; set; }
    }

    private sealed class ThrowyHolder
    {
        public string Boom => throw new InvalidOperationException("getter blew up");
    }
}
