using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

/// <summary>
/// A C# union persisted in the bag: written as its case value, the shape System.Text.Json chose, and read back as the
/// case it held — on the reflection overloads with nothing on the union, on the trim-safe ones with the app's context.
/// </summary>
public sealed partial class PersistentStateUnionTests
{
    public sealed record Search(string Term);

    public sealed record Pick(int Id);

    public union Selection(Search, Pick);

    public union IdOrSlug(int, string);

    public sealed record Named(string Name);

    public sealed record NamedAndAged(string Name, int Age);

    // Every property of Named is also NamedAndAged's, so no set of properties picks Named out.
    public union Indistinct(Named, NamedAndAged);

    [Fact]
    public void A_union_of_records_reads_back_as_the_case_it_held()
    {
        var state = new PersistentState();

        state.Persist<Selection>("search", new Search("boots"));
        state.Persist<Selection>("pick", new Pick(7));

        Assert.True(state.TryGet<Selection>("search", out var search));
        Assert.True(state.TryGet<Selection>("pick", out var pick));
        Assert.Equal(new Search("boots"), search.Value);
        Assert.Equal(new Pick(7), pick.Value);
    }

    [Fact]
    public void A_union_is_stored_as_its_bare_case_value()
    {
        var state = new PersistentState();

        state.Persist<Selection>("search", new Search("boots"));

        Assert.Equal("""{"term":"boots"}""", Encoding.UTF8.GetString(state.Entries["search"]));
    }

    [Fact]
    public void A_union_of_a_number_and_a_string_reads_back_either_case()
    {
        var state = new PersistentState();

        state.Persist<IdOrSlug>("id", 42);
        state.Persist<IdOrSlug>("slug", "42");

        Assert.True(state.TryGet<IdOrSlug>("id", out var id));
        Assert.True(state.TryGet<IdOrSlug>("slug", out var slug));
        Assert.Equal(42, id.Value);
        Assert.Equal("42", slug.Value);
    }

    [Fact]
    public void A_restored_bag_reads_back_the_union_the_original_wrote()
    {
        var original = new PersistentState();
        original.Persist<Selection>("selection", new Pick(3));
        var rebuilt = new PersistentState();

        rebuilt.Restore(original.Entries);

        Assert.True(rebuilt.TryGet<Selection>("selection", out var read));
        Assert.Equal(new Pick(3), read.Value);
    }

    [Fact]
    public void The_typeinfo_overloads_read_the_union_the_reflection_ones_wrote()
    {
        var state = new PersistentState();

        state.Persist<Selection>("selection", new Pick(3));

        Assert.True(state.TryGet("selection", UnionJsonContext.Default.Selection, out var read));
        Assert.Equal(new Pick(3), read.Value);
    }

    [Fact]
    public void A_union_whose_cases_cannot_be_told_apart_still_writes_and_reads_as_a_miss()
    {
        var state = new PersistentState();

        state.Persist<Indistinct>("who", new Named("Ada"));

        Assert.Equal("""{"name":"Ada"}""", Encoding.UTF8.GetString(state.Entries["who"]));
        Assert.False(state.TryGet<Indistinct>("who", out _));
    }

    // The trim-safe route for a union of records: the app's own context names .NET's structural classifier.
    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web, TypeClassifiers = [typeof(JsonUnionTypeStructuralClassifier)])]
    [JsonSerializable(typeof(Selection))]
    private sealed partial class UnionJsonContext : JsonSerializerContext;
}
