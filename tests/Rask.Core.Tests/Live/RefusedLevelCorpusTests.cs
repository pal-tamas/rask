using System.Globalization;

namespace Rask.Core.Tests.Live;

// What the thirty-six changes of RefusedLevelCorpus are answered with, and how many bytes each costs. Run it
// to read the table: every case, the frame that goes out for it, and the whole page beside it.
public sealed class RefusedLevelCorpusTests(ITestOutputHelper output)
{
    public static TheoryData<string> Names => [.. RefusedLevelCorpus.Cases.Keys];

    [Fact]
    public void Every_change_that_puts_one_node_in_the_place_of_another_is_answered_with_the_whole_page()
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
        Assert.All(replies.Values, reply => Assert.Equal(ReplyKind.Page, reply.Kind));
    }

    private static Reply Answer(Func<bool, Component> draw) =>
        Reply.To(RefusedLevelCorpus.Page(draw(false)), RefusedLevelCorpus.Page(draw(true)));
}
