using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
        var json = ConnectionTexts.Json();

        var texts = JsonSerializer.Deserialize<string[]>(json)!;

        Assert.Equal(8, texts.Length);
        Assert.Equal("Reconnecting…", texts[0]);
        Assert.Equal("You're offline — waiting to reconnect…", texts[1]);
        Assert.Equal("Your session timed out. Reload to continue.", texts[7]);
    }

    [Fact]
    public void The_array_is_written_once_and_handed_back_as_it_was()
    {
        var first = ConnectionTexts.Json();

        var second = ConnectionTexts.Json();

        Assert.Same(first, second);
    }

    [Fact]
    public void The_first_response_gets_the_texts_as_the_last_thing_in_the_body()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var html = ConnectionTexts.AddTo("<html><body><p>hi</p></body></html>", services);

        Assert.Matches("^<html><body><p>hi</p><script type=\"application/json\" id=\"rask-texts\" data-rask-managed>\\[[^<]*\\]</script></body></html>$", html);
    }

    [Fact]
    public void A_page_with_no_body_is_handed_back_as_it_was()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var html = ConnectionTexts.AddTo("just text", services);

        Assert.Equal("just text", html);
    }
}
