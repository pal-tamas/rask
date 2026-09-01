using Microsoft.Extensions.DependencyInjection;
using Rask.Blazor.Library.Fixture;
using Rask.Testing;

namespace Rask.Blazor.Tests;

/// <summary>
///     The worked example in <c>docs/blazor-components.md</c>, compiled and rendered.
/// </summary>
/// <remarks>
///     <para>
///         Documentation in this repository is built rather than read, and an example nobody compiles
///         is the one that rots first — it keeps looking right long after the API moved. So the
///         example's component is a real <c>.razor</c> in the fixture library, its island is declared
///         here, and the output the page claims is asserted below.
///     </para>
///     <para>
///         The last test pins the document against the file, so editing one without the other fails
///         rather than drifting. Same reasoning as the front-doors gate over README/NUGET/the hero.
///     </para>
/// </remarks>
public partial class DocExampleTests : global::Rask.Core.RaskMarkup
{
    private static IServiceProvider Services() => new ServiceCollection().BuildServiceProvider();

    private static string DocPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(dir!, "docs", "blazor-components.md");
    }

    private static string FixturePath() =>
        Path.Combine(
            Path.GetDirectoryName(DocPath())!,
            "..",
            "tests",
            "Rask.Blazor.Library.Fixture",
            "PriceTag.razor");

    [Fact]
    public void The_documented_example_renders_what_the_document_says_it_renders()
    {
        var page = RaskTest.Render(
            Quote.Symbol("RASK").Price(12.5m).Tone("up")[Span["watching"]],
            Services());

        // Exactly the markup the doc prints as the result.
        Assert.Contains("<div class=\"price-tag up\">", page.Html, StringComparison.Ordinal);
        Assert.Contains("<strong>RASK</strong>", page.Html, StringComparison.Ordinal);
        Assert.Contains("<span>12.50</span>", page.Html, StringComparison.Ordinal);
        Assert.Contains("<span>watching</span>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Symbol_is_required_and_Tone_is_not()
    {
        // The doc says [EditorRequired] becomes a required step and everything else stays optional.
        var page = RaskTest.Render(Quote.Symbol("RASK"), Services());

        Assert.Contains("<div class=\"price-tag \">", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_document_and_the_compiled_component_are_the_same_code()
    {
        // An example that has drifted from the thing it documents is worse than no example: it reads
        // as verified. Comparing the SUBSTANCE — every non-blank line of the file must appear in the
        // doc — rather than the whole blob, so the doc may still fence and caption it.
        var doc = File.ReadAllText(DocPath());
        var fixture = File.ReadAllLines(FixturePath());

        foreach (var line in fixture)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            Assert.True(
                doc.Contains(trimmed, StringComparison.Ordinal),
                $"docs/blazor-components.md no longer contains this line of PriceTag.razor: {trimmed}");
        }
    }
}

/// <summary>The island in the documented example. Body empty, as the document shows.</summary>
public sealed partial class Quote : BlazorComponent<PriceTag>;
