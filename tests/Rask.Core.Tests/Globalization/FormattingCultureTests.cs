using System.Globalization;
using Rask.Core.Globalization;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Globalization;

/// <summary>
///     A language alone writes its values as its main country does (#1305): <c>"en"</c> names no currency, and a page
///     configured with it wrote <c>{price:C}</c> as <c>¤19.99</c>.
/// </summary>
[Collection(CultureCollection.Name)]
public partial class FormattingCultureTests : global::Rask.Core.RaskMarkup, IDisposable
{
    public FormattingCultureTests() => RaskCulture.IsEnabled = true;

    public void Dispose()
    {
        RaskCulture.ResetForTests();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("hu", "hu-HU")]
    [InlineData("de", "de-DE")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("hu-HU", "hu-HU")]
    [InlineData("", "")]
    public void A_language_alone_is_written_as_its_main_country_and_a_country_as_itself(string configured, string written)
    {
        var culture = CultureInfo.GetCultureInfo(configured);

        var formatting = FormattingCulture.Of(culture);

        Assert.Equal(written, formatting.Name);
    }

    [Theory]
    [InlineData("en", "en-US")]
    [InlineData("hu", "hu-HU")]
    [RestoreCulture]
    public void A_page_in_a_language_alone_writes_its_countrys_currency_and_keeps_the_language(string language, string country)
    {
        var page = new PriceTag();
        page.RenderHandle = new LanguageHandle(CultureInfo.GetCultureInfo(language));

        string html;
        using (LiveRenderContext.Begin(page))
        {
            html = page.ToHtml();
        }

        Assert.Equal($"<p>{19.99m.ToString("C", CultureInfo.GetCultureInfo(country))} {language}</p>", html);
        Assert.DoesNotContain("¤", html, StringComparison.Ordinal);
    }

    [Fact]
    [RestoreCulture]
    public void A_walk_gives_the_thread_back_the_culture_it_borrowed()
    {
        var german = CultureInfo.GetCultureInfo("de-DE");
        CultureInfo.CurrentCulture = german;
        var page = new PriceTag();
        page.RenderHandle = new LanguageHandle(CultureInfo.GetCultureInfo("en"));

        using (LiveRenderContext.Begin(page))
        {
            page.ToHtml();
        }

        Assert.Same(german, CultureInfo.CurrentCulture);
    }

    [Fact]
    public void An_app_that_names_no_language_renders_in_the_threads_culture_untouched()
    {
        RaskCulture.ResetForTests();
        var page = new PriceTag();

        string html;
        using (LiveRenderContext.Begin(page))
        {
            html = page.ToHtml();
        }

        Assert.Equal($"<p>{19.99m.ToString("C", CultureInfo.CurrentCulture)} {CultureInfo.CurrentUICulture.Name}</p>", html);
    }

    private sealed class PriceTag : Component
    {
        protected override Component? Render() => P[$"{19.99m:C} {UICulture.Name}"];
    }

    private sealed class LanguageHandle(CultureInfo language) : IRenderHandle
    {
        public Task RequestRender() => Task.CompletedTask;

        CultureInfo IRenderHandle.Culture => language;

        CultureInfo IRenderHandle.UICulture => language;

        bool IRenderHandle.HasCulture => true;
    }
}
