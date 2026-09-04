using Rask.Wasm;

namespace Rask.Wasm.Tests.Hosting;

// The splice that keeps a prerendered WASM page able to boot.
//
// Prerendering writes into the published wwwroot, where index.html is already the shell the
// WebAssembly SDK filled with the fingerprinted import map, the SRI-pinned preload, the <base href>
// and <script src="main.js">. Writing the rendered document over it produced a page with real markup
// and no way to become interactive — and nothing said so, because every existing test rendered into
// an empty temp directory, which is precisely the path where there is no shell to lose.
public class PrerenderShellTests
{
    // A shell with the parts that actually matter: the SDK's two filled placeholders, the base href,
    // a pre-paint script, a boot placeholder, and the module script that starts the bundle.
    private const string Shell =
        """
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8"/>
            <base href="/"/>
            <title>Rask</title>
            <link rel="preload" id="webassembly" href="_framework/dotnet.abcd1234.js"/>
            <script type="importmap">{"imports":{"./dotnet.js":"./dotnet.abcd1234.js"}}</script>
            <script>window.__preboot = 1;</script>
        </head>
        <body data-rask-root>
        <div class="rask-boot">Loading…</div>
        <script src="main.js" type="module"></script>
        </body>
        </html>
        """;

    private const string Document =
        """
        <!doctype html><html lang="en"><head><meta charset="utf-8"/>
        <title>Rask — the .NET One Person Framework</title>
        <meta name="description" content="Ship a whole product."/>
        <link rel="stylesheet" href="/css/app.css"/></head>
        <body><h1>Ship a whole product.</h1><p>Just you, and C#.</p></body></html>
        """;

    [Fact]
    public void TheBundleCanStillBoot()
    {
        var merged = PrerenderShell.Merge(Shell, Document);

        // The whole point. Each of these is minted by the SDK per publish and is not reproducible from
        // managed code, so losing the shell loses them permanently.
        Assert.Contains("<script src=\"main.js\" type=\"module\"></script>", merged, StringComparison.Ordinal);
        Assert.Contains("type=\"importmap\"", merged, StringComparison.Ordinal);
        Assert.Contains("id=\"webassembly\"", merged, StringComparison.Ordinal);
        Assert.Contains("<base href=\"/\"/>", merged, StringComparison.Ordinal);
        Assert.Contains("window.__preboot", merged, StringComparison.Ordinal);
        Assert.Contains("data-rask-root", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRenderedMarkupReplacesTheBootPlaceholder()
    {
        var merged = PrerenderShell.Merge(Shell, Document);

        Assert.Contains("<h1>Ship a whole product.</h1>", merged, StringComparison.Ordinal);
        Assert.Contains("Just you, and C#.", merged, StringComparison.Ordinal);

        // The spinner is what a crawler used to index. It has no business surviving into a page that
        // now has the real thing.
        Assert.DoesNotContain("rask-boot", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("Loading…", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePagesOwnTitleWins()
    {
        var merged = PrerenderShell.Merge(Shell, Document);

        // A browser takes the FIRST <title>, and the shell's is a placeholder that ships with every
        // page. Appending the document's head without resolving this would leave every prerendered
        // page titled "Rask" — the search result the feature exists to fix.
        Assert.Contains("<title>Rask — the .NET One Person Framework</title>", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("<title>Rask</title>", merged, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(merged, "<title"));
    }

    [Fact]
    public void TheHeadKeepsExactlyOneOfEachSingletonTag()
    {
        var merged = PrerenderShell.Merge(Shell, Document);

        // A second <base> silently wins for every relative URL after it, and a charset that is not the
        // first thing in the head counts for nothing. Both documents carry both.
        Assert.Equal(1, CountOf(merged, "<base"));
        Assert.Equal(1, CountOf(merged, "charset"));
    }

    [Fact]
    public void ThePagesOwnHeadAssetsSurvive()
    {
        var merged = PrerenderShell.Merge(Shell, Document);

        // The head contributions are the SEO payload — the reason for prerendering at all.
        Assert.Contains("name=\"description\"", merged, StringComparison.Ordinal);
        Assert.Contains("/css/app.css", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryScriptInTheShellBodyIsCarriedOverInOrder()
    {
        const string twoScripts =
            """
            <html><head></head><body>
            <div>boot</div>
            <script src="first.js"></script>
            <script src="main.js" type="module"></script>
            </body></html>
            """;

        var merged = PrerenderShell.Merge(twoScripts, Document);

        Assert.True(
            merged.IndexOf("first.js", StringComparison.Ordinal)
            < merged.IndexOf("main.js", StringComparison.Ordinal),
            "the shell's scripts must keep their order — a bundle that boots before its polyfill is a race");
    }

    [Fact]
    public void ADocumentWithNoShellToSpliceIntoIsReturnedWhole()
    {
        // A caller driving its own pass may have no shell at all. Returning the document is worth more
        // than failing the publish; the callers that DO have a shell are the ones that would notice.
        Assert.Equal(Document, PrerenderShell.Merge("not a document", Document));
    }

    [Fact]
    public void ABodyEndTagInsideAScriptDoesNotTruncateTheShell()
    {
        // The end tag is found from the END of the document for this reason. Scanning forward would
        // close the body on the string below and drop the boot script that follows it.
        const string trickyShell =
            """
            <html><head></head><body>
            <script>var t = "</body>";</script>
            <script src="main.js" type="module"></script>
            </body></html>
            """;

        var merged = PrerenderShell.Merge(trickyShell, Document);

        Assert.Contains("main.js", merged, StringComparison.Ordinal);
    }

    private static int CountOf(string haystack, string needle)
    {
        var count = 0;
        var cursor = 0;
        while (true)
        {
            var hit = haystack.IndexOf(needle, cursor, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                return count;
            }

            count++;
            cursor = hit + needle.Length;
        }
    }
}
