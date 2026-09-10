using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.External;
using Xunit;

namespace Rask.Generators.Tests;

/// <summary>
///     Where an island's manifest comes from (#939).
/// </summary>
/// <remarks>
///     An island used to have to be bundled by the APP that serves the page: the client resolved its
///     manifest from a hard-coded, app-rooted path, so a class library's bundle — served under
///     <c>_content/&lt;PackageId&gt;/</c> — landed where nothing looked for it. The build now derives the
///     manifest URL per project kind and hands it to the generator, which writes it onto the component
///     and from there onto every host element.
/// </remarks>
public class ExternalManifestUrlTests
{
    private const string Island = """
        namespace Demo;

        public sealed partial class Chart : Rask.External.ReactComponent
        {
            public string? Heading { get; set; }
        }
        """;

    [Fact]
    public void A_library_manifest_url_is_written_onto_the_component()
    {
        var generated = Generate("/_content/Acme.Ui/_rask/external/manifest.json");

        Assert.Contains(
            "protected override string? ManifestUrl => \"/_content/Acme.Ui/_rask/external/manifest.json\";",
            generated,
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void An_app_generates_no_manifest_override_at_all()
    {
        // The app's own manifest is what the client already assumes, so an island in an app must write
        // nothing: no override, and therefore no attribute on the host element. Stamping the same
        // string on every island of every app would be noise in the markup and on the wire.
        var generated = Generate("/_rask/external/manifest.json");

        Assert.DoesNotContain("ManifestUrl", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void A_build_that_says_nothing_generates_no_override_either()
    {
        // An older Rask.External's targets do not set the property. Falling back to "no override" keeps
        // such a project building and behaving exactly as it did.
        var generated = Generate(null);

        Assert.DoesNotContain("ManifestUrl", generated, System.StringComparison.Ordinal);
    }

    private static string Generate(string? manifestUrl)
    {
        var run = GeneratorDriverFixture.Run(
            [("/proj/Chart.cs", Island)],
            [new ExternalGenerator()],
            analyzerConfigOptions: new Options(manifestUrl));

        return string.Concat(run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
    }

    private sealed class Options(string? manifestUrl) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Global(manifestUrl);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class Global(string? manifestUrl) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
            {
                if (key == "build_property.RaskExternalManifestUrl" && manifestUrl is not null)
                {
                    value = manifestUrl;
                    return true;
                }

                value = null;
                return false;
            }
        }
    }
}
