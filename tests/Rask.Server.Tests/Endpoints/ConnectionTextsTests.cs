using System.Text.Json;
using Rask.Server.Http;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     The reconnect overlay's words, as the runtime's script carries them: one array, in the order <c>rask.js</c>
///     reads by index, in English when no language is configured.
/// </summary>
public sealed class ConnectionTextsTests
{
    [Fact]
    public void With_no_language_configured_the_words_are_the_english_the_runtime_falls_back_to()
    {
        var json = ConnectionTexts.Attribute()["rask-texts"]!;

        var texts = JsonSerializer.Deserialize<string[]>(json)!;

        Assert.Equal(8, texts.Length);
        Assert.Equal("Reconnecting…", texts[0]);
        Assert.Equal("You're offline — waiting to reconnect…", texts[1]);
        Assert.Equal("Your session timed out. Reload to continue.", texts[7]);
    }

    [Fact]
    public void The_attribute_is_built_once_and_handed_back_as_it_was()
    {
        var first = ConnectionTexts.Attribute();

        var second = ConnectionTexts.Attribute();

        Assert.Same(first, second);
    }
}
