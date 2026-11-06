using System.Text.Json;

namespace Rask.Core.Tests.Live;

/// <summary>
///     The one hidden bound field of a group the browser owns (<c>rask-bound.ts</c>) under a render that lands as a
///     morph: the value the page rendered last, rendered again, is no news, and what the reader made of the group stands.
/// </summary>
/// <remarks>
///     Drives the production <c>rask-bound.ts</c> in-process on Jint (<c>BoundFixture.ts</c>). The WebAssembly host
///     morphs every render, and a held select's pick was wiped by the render that took a message away from under it;
///     the journey on a real page is the floating-labels form in <c>WasmExampleTests</c> (Rask.Site.E2E.Tests).
/// </remarks>
public sealed class BoundFieldClientTests
{
    private static JsonElement Run() => ScriptFixture.Run("BoundFixture");

    private static bool Read(JsonElement root, string name) => root.GetProperty(name).GetBoolean();

    [Fact]
    public void A_render_that_puts_back_the_value_it_rendered_last_leaves_what_the_reader_picked()
    {
        var root = Run();

        var kept = Read(root, "keptAcrossARenderThatSaidNothingNew");
        var echo = Read(root, "echoIsNoNews");

        Assert.True(kept);
        Assert.True(echo);
    }

    [Fact]
    public void A_render_with_an_answer_of_its_own_is_adopted_and_is_no_news_the_next_time()
    {
        var root = Run();

        var adopted = Read(root, "aNewAnswerIsAdopted");
        var again = Read(root, "theNewAnswerRenderedAgainIsNoNewsEither");

        Assert.True(adopted);
        Assert.True(again);
    }
}
