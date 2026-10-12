using System.Globalization;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// What the thirty-six changes of RefusedLevelCorpus are answered with, and how many bytes each costs. Run it
// to read the table: every case, the frame that goes out for it, and the whole page beside it. Before the
// differ morphed a refused level alone, all thirty-six were whole pages: 95,838 B. They are 9,248 B now.
public sealed class RefusedLevelCorpusTests(ITestOutputHelper output)
{
    public static TheoryData<string> Names => [.. RefusedLevelCorpus.Cases.Keys];

    [Fact]
    public void Every_change_that_puts_one_node_in_the_place_of_another_is_answered_with_a_morph_a_tenth_the_size_of_the_pages()
    {
        var replies = RefusedLevelCorpus.Cases.ToDictionary(c => c.Key, c => Answer(c.Value.Draw), StringComparer.Ordinal);

        foreach (var (name, reply) in replies)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{RefusedLevelCorpus.Cases[name].Category,-18} {reply.Kind,-5} {reply.Bytes,6} B of {reply.PageBytes,6} B  {name}"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{replies.Count} cases: {replies.Values.Sum(r => r.Bytes)} B sent, {replies.Values.Sum(r => r.PageBytes)} B as whole pages"));
        Assert.Equal(36, replies.Count);
        Assert.All(replies.Values, reply => Assert.Equal(ReplyKind.Morph, reply.Kind));
        Assert.True(replies.Values.Sum(r => r.Bytes) * 10 < replies.Values.Sum(r => r.PageBytes));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_morph_names_an_element_of_the_content_never_the_page_around_it_and_leaves_the_new_render(string name)
    {
        var reply = Answer(RefusedLevelCorpus.Cases[name].Draw);
        var search = reply.Before.At(0, 1, 1);

        reply.Apply();

        var morph = Assert.Single(reply.Ops, op => op.Kind == EditOpKind.MorphSubtree);
        Assert.True(morph.Path.Length >= 3 && morph.Path[..2] is [0, 2], "the morph is of " + string.Join('/', morph.Path));
        Assert.DoesNotContain("Menu item", Assert.Single(reply.Morphed), StringComparison.Ordinal);
        Assert.Same(search, reply.Before.At(0, 1, 1));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    private static Reply Answer(Func<bool, Component> draw) =>
        Reply.To(RefusedLevelCorpus.Page(draw(false)), RefusedLevelCorpus.Page(draw(true)));
}
