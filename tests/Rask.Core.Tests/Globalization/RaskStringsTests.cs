using System.Globalization;
using Rask.Core.Globalization;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Globalization;

// The framework's own text: the app's translation first, then a library's, then the English at the call
// site. One process-wide chain, so these share CultureFlowTests' collection and never overlap it.
[Collection(CultureCollection.Name)]
public partial class RaskStringsTests : global::Rask.Core.RaskMarkup, IDisposable
{
    private static readonly CultureInfo Hungarian = CultureInfo.GetCultureInfo("hu-HU");

    public RaskStringsTests() => RaskCulture.IsEnabled = true;

    public void Dispose()
    {
        RaskStrings.ResetForTests();
        RaskCulture.ResetForTests();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [RestoreCulture]
    public void With_no_source_the_english_is_written_with_its_values()
    {
        CultureInfo.CurrentUICulture = Hungarian;

        var text = RaskStrings.Get(RaskString.PaginationSummary, "Showing {0} to {1} of {2} results", 1, 10, 13);

        Assert.Equal("Showing 1 to 10 of 13 results", text);
    }

    [Fact]
    [RestoreCulture]
    public void A_translation_puts_the_values_in_its_own_order()
    {
        CultureInfo.CurrentUICulture = Hungarian;
        RaskStrings.UseSource(new Table("hu", (RaskString.PaginationSummary, "Összesen {2}, ebből {0}–{1}")));

        var text = RaskStrings.Get(RaskString.PaginationSummary, "Showing {0} to {1} of {2} results", 1, 10, 13);

        Assert.Equal("Összesen 13, ebből 1–10", text);
    }

    [Fact]
    [RestoreCulture]
    public void A_translated_value_is_written_in_the_visitors_culture()
    {
        (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (Hungarian, Hungarian);
        RaskStrings.UseSource(new Table("hu", (RaskString.RatingValue, "{0:N1} / {1}")));

        var text = RaskStrings.Get(RaskString.RatingValue, "{0} of {1}", 2.5, 5);

        Assert.Equal("2,5 / 5", text);
    }

    [Fact]
    [RestoreCulture]
    public void The_english_writes_its_values_the_same_in_every_culture()
    {
        (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = (Hungarian, Hungarian);
        RaskStrings.UseSource(new Table("hu"));

        var text = RaskStrings.Get(RaskString.RatingValue, "{0} of {1}", 2.5, 5);

        Assert.Equal("2.5 of 5", text);
    }

    [Theory]
    [InlineData("{0} / {1} / {2}")]
    [InlineData("{0} of {")]
    [RestoreCulture]
    public void A_translation_the_text_cannot_fill_is_passed_over_for_the_english(string broken)
    {
        CultureInfo.CurrentUICulture = Hungarian;
        RaskStrings.UseSource(new Table("hu", (RaskString.RatingValue, broken)));

        var text = RaskStrings.Get(RaskString.RatingValue, "{0} of {1}", 2, 5);

        Assert.Equal("2 of 5", text);
    }

    [Fact]
    [RestoreCulture]
    public void A_library_translates_what_the_app_has_not()
    {
        CultureInfo.CurrentUICulture = Hungarian;
        RaskStrings.UseLibrarySource(new Table("hu", (RaskString.SelectEmpty, "Nincs találat"), (RaskString.ModalClose, "Ablak bezárása")));
        RaskStrings.UseSource(new Table("hu", (RaskString.ModalClose, "Bezárás")));

        var fromLibrary = RaskStrings.Get(RaskString.SelectEmpty, "No results found");
        var fromApp = RaskStrings.Get(RaskString.ModalClose, "Close modal");
        var fromNeither = RaskStrings.Get(RaskString.SelectLoading, "Loading...");

        Assert.Equal("Nincs találat", fromLibrary);
        Assert.Equal("Bezárás", fromApp);
        Assert.Equal("Loading...", fromNeither);
    }

    [Fact]
    [RestoreCulture]
    public void A_library_is_silent_until_the_app_names_its_languages()
    {
        // Without that list the language is the machine's: an English app on a Hungarian server would
        // otherwise draw a Hungarian pager.
        CultureInfo.CurrentUICulture = Hungarian;
        RaskStrings.UseLibrarySource(new Table("hu", (RaskString.SelectEmpty, "Nincs találat")));
        RaskCulture.ResetForTests();

        var text = RaskStrings.Get(RaskString.SelectEmpty, "No results found");

        Assert.Equal("No results found", text);
    }

    [Fact]
    [RestoreCulture]
    public void Reading_a_library_translation_marks_the_component_so_a_language_switch_repaints_it()
    {
        RaskStrings.UseLibrarySource(new Table("hu", (RaskString.SelectEmpty, "Nincs találat")));
        var reader = new EmptyRow { RenderHandle = new FixedCultureHandle(Hungarian) };

        string html;
        using (LiveRenderContext.Begin(reader))
        {
            html = System.Net.WebUtility.HtmlDecode(reader.ToHtml());
        }

        Assert.Contains("Nincs találat", html, StringComparison.Ordinal);
        Assert.True(reader.ReadsAmbientStateInternal);
    }

    private sealed class Table(string culture, params (RaskString Key, string Text)[] texts) : IRaskStringSource
    {
        public string? Get(RaskString key, string cultureTag) =>
            cultureTag.StartsWith(culture, StringComparison.Ordinal)
                ? texts.FirstOrDefault(text => text.Key == key).Text
                : null;
    }

    private sealed class FixedCultureHandle(CultureInfo culture) : IRenderHandle
    {
        public Task RequestRender() => Task.CompletedTask;

        CultureInfo IRenderHandle.Culture => culture;
        CultureInfo IRenderHandle.UICulture => culture;
    }

    private sealed class EmptyRow : Component
    {
        protected override Component Render() => Div[RaskStrings.Get(RaskString.SelectEmpty, "No results found")];
    }
}
