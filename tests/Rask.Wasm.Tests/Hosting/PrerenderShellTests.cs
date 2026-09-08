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
    public void AShellThatEXPLAINSItselfIsStillSplicedCorrectly()
    {
        // The shape that broke it: a comment at the top of the shell — the natural thing to write in a
        // file the build rewrites — mentioning <head> and <html> in prose. The tag search had no notion
        // of comments, locked onto the one in the sentence, and measured everything from inside it. The
        // prefix handed to the attribute merge then contained no <html> at all, so every attribute a
        // Shell override set on it was silently dropped.
        //
        // It read as working for years because the site had already moved data-rask-ui into the shell's
        // own literal tag after being bitten once, so the one attribute anyone watched survived for an
        // unrelated reason.
        const string Commented =
            """
            <!--
              This shell is the TEMPLATE the prerendered page is spliced into: the pass keeps this <head>
              and replaces the body below. Keep it minimal; the App component owns the real <html> tag.
            -->
            <!doctype html>
            <html lang="en" data-shell="yes">
            <head><title>Shell</title></head>
            <body><div class="rask-boot">Loading</div><script src="main.js" type="module"></script></body>
            </html>
            """;

        var merged = PrerenderShell.Merge(
            Commented,
            "<!doctype html><html lang=\"en\" data-theme=\"dark\"><head><title>Real</title></head>"
            + "<body><p>real</p></body></html>");

        var tag = merged[merged.IndexOf("<html lang", StringComparison.Ordinal)..];
        tag = tag[..tag.IndexOf('>')];

        // The document's own <html> attribute survived the splice...
        Assert.Contains("data-theme=\"dark\"", tag, StringComparison.Ordinal);
        // ...alongside the shell's, which wins on conflict and is why lang is still the shell's.
        Assert.Contains("data-shell=\"yes\"", tag, StringComparison.Ordinal);
        Assert.Contains(PrerenderShell.PrerenderedAttribute, tag, StringComparison.Ordinal);

        // And the rest of the splice still happened: the page's body replaced the boot placeholder,
        // the page's title replaced the shell's, and the boot script came along.
        Assert.Contains("<p>real</p>", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("rask-boot", merged, StringComparison.Ordinal);
        Assert.Contains("<title>Real</title>", merged, StringComparison.Ordinal);
        Assert.DoesNotContain("<title>Shell</title>", merged, StringComparison.Ordinal);
        Assert.Contains("main.js", merged, StringComparison.Ordinal);
    }

    [Fact]
    public void ASplicedPageIsMarkedAsPrerendered()
    {
        // The boot script needs to tell a page that already has its content from a shell that has none,
        // because it changes what booting is FOR: on a shell the runtime is all there is, and on a
        // prerendered page starting it before the browser has taken a frame is what turned a
        // largest-contentful-paint of ~5s into 37.8s, on an element that was in the HTML all along.
        //
        // Stamped by the splice rather than inferred by the client. "Is the boot spinner missing" is the
        // same question most of the time and not always — a hand-written shell need not have one.
        var merged = PrerenderShell.Merge(
            Shell,
            "<!doctype html><html lang=\"en\"><head><title>T</title></head><body><p>real</p></body></html>");

        var tag = merged[merged.IndexOf("<html", StringComparison.Ordinal)..];
        tag = tag[..tag.IndexOf('>')];

        Assert.Contains(PrerenderShell.PrerenderedAttribute, tag, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkerIsNotAddedTwiceWhenTheShellAlreadyCarriesIt()
    {
        // A republish over an already-prerendered wwwroot reads its own previous output as the shell.
        // Two copies of an attribute is not fatal, but it is the kind of thing that grows one per
        // publish until someone notices a line of them.
        var once = PrerenderShell.Merge(
            Shell.Replace("<html lang=\"en\">", $"<html lang=\"en\" {PrerenderShell.PrerenderedAttribute}>",
                StringComparison.Ordinal),
            "<!doctype html><html lang=\"en\"><head></head><body><p>real</p></body></html>");

        var tag = once[once.IndexOf("<html", StringComparison.Ordinal)..];
        tag = tag[..tag.IndexOf('>')];

        Assert.Equal(1, Occurrences(tag, PrerenderShell.PrerenderedAttribute));
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal);
             i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    // Regression: the landing site set data-rask-ui on <html> through its Shell override to turn the
    // component kit's theme on, and shipped to production with every colour computing to nothing —
    // structurally perfect, entirely grey — because the merge kept the SDK's opening tag and dropped
    // the render's attributes. Layout survives that, which is what made it so quiet.
    [Fact]
    public void TheDocumentsHtmlAttributesSurviveTheMerge()
    {
        const string themed =
            """
            <!doctype html><html lang="en" data-rask-ui dir="rtl"><head><title>T</title></head>
            <body><h1>Hi</h1></body></html>
            """;

        var merged = PrerenderShell.Merge(Shell, themed);

        // On the opening <html> tag, not merely somewhere in the document.
        var open = merged.IndexOf("<html", StringComparison.Ordinal);
        var gt = merged.IndexOf('>', open);
        var tag = merged[open..gt];

        Assert.Contains("data-rask-ui", tag, StringComparison.Ordinal);
        Assert.Contains("dir=\"rtl\"", tag, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShellWinsWhereBothNameTheSameHtmlAttribute()
    {
        // The shell's lang is the one computed for THIS publish; a render that disagrees did not know
        // about it. Only attributes the shell lacks are added.
        const string other =
            """
            <!doctype html><html lang="de" data-rask-ui><head><title>T</title></head>
            <body><h1>Hi</h1></body></html>
            """;

        var merged = PrerenderShell.Merge(Shell, other);

        var open = merged.IndexOf("<html", StringComparison.Ordinal);
        var gt = merged.IndexOf('>', open);
        var tag = merged[open..gt];

        Assert.Contains("lang=\"en\"", tag, StringComparison.Ordinal);
        Assert.DoesNotContain("lang=\"de\"", tag, StringComparison.Ordinal);
        Assert.Contains("data-rask-ui", tag, StringComparison.Ordinal);
    }

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
