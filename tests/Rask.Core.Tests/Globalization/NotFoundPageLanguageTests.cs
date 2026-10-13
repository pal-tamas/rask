using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;
using Rask.Core.Live;
using Rask.Core.Routing;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Globalization;

// The built-in not-found page says its heading, its sentence, its link and its tab's title in the app's
// language: three catalog keys, the sentence carrying the path as its value. The strings' source is
// process-wide, so this shares CultureFlowTests' collection.
[Collection(CultureCollection.Name)]
public partial class NotFoundPageLanguageTests : global::Rask.Core.RaskMarkup, IDisposable
{
    public NotFoundPageLanguageTests() => RaskCulture.IsEnabled = true;

    public void Dispose()
    {
        RaskStrings.ResetForTests();
        RaskCulture.ResetForTests();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [RestoreCulture]
    public void A_translated_page_says_its_heading_its_sentence_its_link_and_its_title_in_that_language()
    {
        RaskStrings.UseSource(new Table(
            (RaskString.NotFoundTitle, "Az oldal nem található"),
            (RaskString.NotFoundBody, "Ezen a címen nincs oldal: {0}"),
            (RaskString.NotFoundBackHome, "Vissza a kezdőlapra")));

        var html = Document("/nincs-ilyen");

        Assert.Equal("Az oldal nem található", Regex.Match(html, "<title[^>]*>([^<]*)<").Groups[1].Value);
        Assert.Matches("<h1[^>]*>Az oldal nem található</h1>", html);
        Assert.Matches("Ezen a címen nincs oldal: <code[^>]*>/nincs-ilyen</code></p>", html);
        Assert.Matches("<a[^>]*href=\"/\"[^>]*>Vissza a kezdőlapra</a>", html);
        Assert.DoesNotContain("Page not found", html, StringComparison.Ordinal);
        Assert.DoesNotContain("No route", html, StringComparison.Ordinal);
    }

    [Fact]
    [RestoreCulture]
    public void A_translation_puts_the_path_where_its_sentence_wants_it()
    {
        RaskStrings.UseSource(new Table((RaskString.NotFoundBody, "A(z) {0} címen nincs oldal.")));

        var html = Document("/nincs-ilyen");

        Assert.Matches("A\\(z\\) <code[^>]*>/nincs-ilyen</code> címen nincs oldal\\.</p>", html);
    }

    [Fact]
    [RestoreCulture]
    public void A_catalog_entry_written_before_the_sentence_carried_the_path_still_shows_the_path_after_it()
    {
        RaskStrings.UseSource(new Table((RaskString.NotFoundBody, "Nincs ilyen útvonal: ")));

        var html = Document("/nincs-ilyen");

        Assert.Matches("Nincs ilyen útvonal: <code[^>]*>/nincs-ilyen</code>\\.</p>", html);
    }

    private static string Document(string path)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("hu-HU");
        var services = new ServiceCollection().AddSingleton(new RouteState { Path = path }).BuildServiceProvider();

        // The serializer writes a letter outside ASCII as a character reference: read as a browser shows it.
        return System.Net.WebUtility.HtmlDecode(
            new RootErrorBoundary(new StubComponent(() => DefaultNotFoundPage)).RenderAsLiveRoot(services));
    }

    private sealed class Table(params (RaskString Key, string Text)[] texts) : IRaskStringSource
    {
        public string? Get(RaskString key, string cultureTag) =>
            cultureTag.StartsWith("hu", StringComparison.Ordinal) ? texts.FirstOrDefault(text => text.Key == key).Text : null;
    }
}
