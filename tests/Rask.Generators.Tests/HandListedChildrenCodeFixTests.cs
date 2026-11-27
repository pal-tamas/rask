using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK192 quick-fix: an aggregate's children held by hand become one HasMany&lt;X&gt;.</summary>
public class HandListedChildrenCodeFixTests
{
    private static string Source(string members) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Rask.Data;
        namespace Armory;
        public sealed class Pistol : Entity<Guid>
        {
            public int Calibre { get; private set; }
        }
        public sealed class Rack : Aggregate<Guid>
        {
            {{members}}
        }
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new HandListedChildrenAnalyzer(), new HandListedChildrenCodeFixProvider(), "RASK192", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    private static Task<bool> Offered(string source) =>
        CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new HandListedChildrenAnalyzer(), new HandListedChildrenCodeFixProvider(), "RASK192", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    [Fact]
    public async Task The_fix_turns_a_private_list_and_its_view_into_one_HasMany_and_its_uses_into_the_propertys()
    {
        var source = Source("""
            private readonly List<Pistol> _pistols = [];

            /// <summary>The pistols on the rack.</summary>
            public IReadOnlyList<Pistol> Pistols => _pistols;

            public void Hang(Pistol p) => _pistols.Add(p);
            public void HangAll(IEnumerable<Pistol> ps) => _pistols.AddRange(ps);
            public bool Take(Pistol p) => this._pistols.Remove(p);
            public int Small() => _pistols.RemoveAll(p => p.Calibre < 9);
            public void Empty() => _pistols.Clear();
            public int Heaviest() => _pistols.Count == 0 ? 0 : _pistols.Max(p => p.Calibre) + _pistols[0].Calibre;
            """);

        var fixed_ = await Fix(source);

        Assert.Contains("/// <summary>The pistols on the rack.</summary>", fixed_, StringComparison.Ordinal);
        Assert.Contains("public HasMany<Pistol> Pistols { get; } = new();", fixed_, StringComparison.Ordinal);
        Assert.Contains("public void Hang(Pistol p) => Pistols.Add(p);", fixed_, StringComparison.Ordinal);
        Assert.Contains("public void HangAll(IEnumerable<Pistol> ps) => Pistols.Add(ps);", fixed_, StringComparison.Ordinal);
        Assert.Contains("public bool Take(Pistol p) => this.Pistols.Remove(p);", fixed_, StringComparison.Ordinal);
        Assert.Contains("Pistols.RemoveAll(p => p.Calibre < 9)", fixed_, StringComparison.Ordinal);
        Assert.Contains("Pistols.Clear()", fixed_, StringComparison.Ordinal);
        Assert.Contains("Pistols.Count == 0 ? 0 : Pistols.Max(p => p.Calibre) + Pistols[0].Calibre", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("_pistols", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_fix_turns_a_public_list_into_a_HasMany_and_its_AddRange_into_Add()
    {
        var source = Source("""
            public List<Pistol> Pistols { get; private set; } = [];
            public void HangAll(IEnumerable<Pistol> ps) => Pistols.AddRange(ps);
            """);

        var fixed_ = await Fix(source);

        Assert.Contains("public HasMany<Pistol> Pistols { get; } = new();", fixed_, StringComparison.Ordinal);
        Assert.Contains("=> Pistols.Add(ps);", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("List<Pistol>", fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private readonly List<Pistol> _pistols = []; public IReadOnlyList<Pistol> Pistols => _pistols; public void First(Pistol p) => _pistols.Insert(0, p);")]
    [InlineData("private readonly List<Pistol> _pistols = []; public IReadOnlyList<Pistol> Pistols => _pistols; public void Swap(Pistol p) => _pistols[0] = p;")]
    [InlineData("private readonly List<Pistol> _pistols = []; public IReadOnlyList<Pistol> Pistols => _pistols; public void Keep(List<Pistol> into) => Keep(_pistols);")]
    [InlineData("private readonly List<Pistol> _pistols = []; public IReadOnlyList<Pistol> Pistols => _pistols; public int Drop() => _pistols.RemoveAll(IsSmall); private static bool IsSmall(Pistol p) => p.Calibre < 9;")]
    [InlineData("private List<Pistol> _pistols = []; public IReadOnlyList<Pistol> Pistols => _pistols; public void Reset() => _pistols = [];")]
    [InlineData("private readonly List<Pistol> _pistols = [new()]; public IReadOnlyList<Pistol> Pistols => _pistols;")]
    [InlineData("public List<Pistol> Pistols { get; private set; } = []; public void Reset() => Pistols = [];")]
    public async Task The_fix_is_withheld_where_HasMany_would_not_do_what_the_list_did(string members)
    {
        var source = Source(members);

        var offered = await Offered(source);

        Assert.False(offered);
    }
}
