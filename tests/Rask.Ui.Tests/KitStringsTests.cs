using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Globalization;
using Rask.UiTests.Flux;

namespace Rask.UiTests;

/// <summary>
///     The kit's fixed text — a pager's summary, a select's "No results found", the name of a close button —
///     is read from the <c>RaskStrings</c> catalog: the app's translation, then the kit's own Hungarian, then
///     the English at the call site.
/// </summary>
public partial class KitStringsTests : global::Rask.Core.RaskMarkup
{
    private static readonly string Kit = Path.Combine(RepoRoot.FullPath, "src", "Rask.Ui");

    // Prose a kit source may hold that no visitor reads as language. Anything else must go through RaskStrings.
    private static readonly HashSet<string> NotText = new(StringComparer.Ordinal)
    {
        // Key caps and key lists.
        "Ctrl", "Win", "Alt", "Shift", "Enter ArrowUp ArrowDown Home End PageUp PageDown", "Enter Space ArrowUp ArrowDown",
        "Space ArrowUp ArrowDown",
        // A mode's name in a developer's exception message, and the shape of a link rather than a word.
        "Single", "https://...",
    };

    [Fact]
    public void A_visitor_who_reads_english_sees_what_flux_shows()
    {
        var pager = Ui.Pagination.Paginator(new UiPaginator { Page = 1, PerPage = 10, Total = 13 });

        var html = KitCulture.In("en-US", pager.ToHtml).AsText();

        Assert.Contains("Showing 1 to 10 of 13 results", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"« Previous\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Next »\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hungarian_visitor_gets_a_hungarian_pager_with_no_catalog_in_the_app()
    {
        var pager = Ui.Pagination.Paginator(new UiPaginator { Page = 2, PerPage = 10, Total = 13 });

        var html = KitCulture.In("hu-HU", pager.ToHtml).AsText();

        Assert.Contains("13 találatból 11–13.", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"« Előző\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Következő »\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Showing", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("select", "Nincs találat", "No results found")]
    [InlineData("select", "Keresés…", "Search...")]
    [InlineData("date-picker", "Válassz dátumot", "Select a date")]
    [InlineData("date-picker", "Válassz időszakot", "Select a date range")]
    [InlineData("date-picker", "Elmúlt 7 nap", "Last 7 Days")]
    [InlineData("date-picker", "Mégse", "Cancel")]
    [InlineData("calendar", "Következő hónap", "Next month")]
    [InlineData("time-picker", "Válassz időpontot", "Select a time")]
    [InlineData("editor", "Félkövér", "Bold")]
    [InlineData("editor", "Formázott szövegszerkesztő", "Rich text editor")]
    [InlineData("input", "Mező törlése", "Clear input")]
    [InlineData("modal", "Ablak bezárása", "Close modal")]
    [InlineData("file-upload", "Fájl eltávolítása", "Remove file")]
    [InlineData("otp-input", "1. karakter, összesen 6", "Character 1 of 6")]
    [InlineData("slider", ", a tartomány eleje", " start range")]
    public void The_kit_speaks_hungarian_wherever_flux_writes_its_own_text(string page, string hungarian, string english)
    {
        var parity = Parities().Single(parity => string.Equals(parity.Page, page, StringComparison.Ordinal));

        var inEnglish = KitCulture.In("en-US", () => Render(parity));
        var inHungarian = KitCulture.In("hu-HU", () => Render(parity));

        Assert.Contains(english, inEnglish, StringComparison.Ordinal);
        Assert.Contains(hungarian, inHungarian, StringComparison.Ordinal);
    }

    [Fact]
    public void The_leave_dialog_asks_each_visitor_in_their_own_language()
    {
        // That a switch of language repaints a drawn one is the mark RaskStrings leaves on its reader,
        // which RaskStringsTests in Rask.Core.Tests holds.
        var first = KitCulture.In("en-US", () => Ui.ConfirmLeave.ToHtml());
        var switched = KitCulture.In("hu-HU", () => Ui.ConfirmLeave.ToHtml());

        Assert.Contains(">Stay<", first, StringComparison.Ordinal);
        Assert.Contains(">Leave<", first, StringComparison.Ordinal);
        Assert.Contains(">Maradok<", switched, StringComparison.Ordinal);
        Assert.Contains(">Elhagyom<", switched, StringComparison.Ordinal);
        Assert.DoesNotContain(">Stay<", switched, StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_app_passes_in_is_said_as_given_in_every_language()
    {
        var dialog = Ui.ConfirmLeave.Stay("Nem").Leave("Igen");

        var html = KitCulture.In("hu-HU", dialog.ToHtml);

        Assert.Contains(">Nem<", html, StringComparison.Ordinal);
        Assert.Contains(">Igen<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_in_the_apps_own_catalog_is_used_over_the_kits()
    {
        // Slovakia's Hungarian, which no other test reads: the app's source is process-wide.
        RaskStrings.UseSource(new AppCatalog("hu-SK", RaskString.PaginationPrevious, "Vissza"));
        var pager = Ui.Pagination.Paginator(new UiPaginator { Page = 2, PerPage = 10, Total = 13 });

        var html = KitCulture.In("hu-SK", pager.ToHtml).AsText();

        Assert.Contains("aria-label=\"Vissza\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Következő »\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Asking_in_a_language_the_kit_has_no_catalog_for_allocates_nothing()
    {
        // The render path of every English page: the kit's Hungarian is registered and is not the answer.
        var kitIsLoaded = Ui.ConfirmLeave.ToHtml();
        var warm = KitCulture.In("en-US", () => RaskStrings.Get(RaskString.SelectEmpty, "No results found"));

        var allocated = KitCulture.In("en-US", () =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
            {
                _ = RaskStrings.Get(RaskString.SelectEmpty, "No results found");
            }

            return (GC.GetAllocatedBytesForCurrentThread() - before).ToString(System.Globalization.CultureInfo.InvariantCulture);
        });

        Assert.NotEmpty(kitIsLoaded);
        Assert.Equal("No results found", warm);
        Assert.Equal("0", allocated);
    }

    [Fact]
    public void Every_key_the_kit_reads_has_a_hungarian_and_the_file_holds_no_other()
    {
        var read = KitEnglish().Keys.Order(StringComparer.Ordinal);

        var translated = KitHungarian().Keys.Order(StringComparer.Ordinal);

        Assert.Equal(read, translated);
    }

    [Fact]
    public void Every_key_is_documented_with_the_english_its_call_site_writes()
    {
        var docs = File.ReadAllText(Path.Combine(RepoRoot.FullPath, "docs", "localization.md"));
        var documented = DocumentedRow().Matches(docs).ToDictionary(row => row.Groups[1].Value, row => row.Groups[2].Value, StringComparer.Ordinal);

        var written = KitEnglish();

        Assert.Equal(Enum.GetNames<RaskString>().Order(StringComparer.Ordinal), documented.Keys.Order(StringComparer.Ordinal));
        Assert.All(written, text => Assert.Equal(documented[text.Key], text.Value));
    }

    [Fact]
    public void A_key_says_the_same_english_wherever_the_kit_reads_it()
    {
        var sites = KitSources().SelectMany(source => CallSite().Matches(source.Routed).Select(site => (Key: site.Groups[1].Value, English: site.Groups[2].Value)));

        var disagreeing = sites.GroupBy(site => site.Key, StringComparer.Ordinal)
            .Where(key => key.Select(site => site.English).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(key => key.Key);

        Assert.Empty(disagreeing);
    }

    [Fact]
    public void No_kit_source_writes_english_prose_past_the_catalog()
    {
        // What this sees: a string literal that reads as English prose (a capitalised word, then more or
        // nothing), and any literal handed straight to a name, a placeholder, a title, an alt or a tooltip.
        // What it cannot: text in lower case that reaches the page through a variable ("selected", "mm"),
        // text put together from pieces, and anything a script or a stylesheet writes.
        var stray = new List<string>();
        foreach (var (file, _, unrouted) in KitSources())
        {
            stray.AddRange(
                from literal in Prose().Matches(unrouted).Concat(Sink().Matches(unrouted))
                let text = literal.Groups[1].Value
                where text.Length > 0 && !NotText.Contains(text)
                select $"{file}: \"{text}\"");
        }

        Assert.Empty(stray.Distinct(StringComparer.Ordinal));
    }

    private static List<FluxParity> Parities() =>
        typeof(FluxParity).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(FluxParity)))
            .Select(type => (FluxParity)Activator.CreateInstance(type)!)
            .ToList();

    private static string Render(FluxParity parity) =>
        string.Concat(parity.Examples().Select(example => example.Example.ToHtml())).AsText();

    // Key → English, from every RaskStrings.Get in the kit's sources.
    private static Dictionary<string, string> KitEnglish() =>
        KitSources()
            .SelectMany(source => CallSite().Matches(source.Routed))
            .GroupBy(site => site.Groups[1].Value, StringComparer.Ordinal)
            .ToDictionary(key => key.Key, key => key.First().Groups[2].Value, StringComparer.Ordinal);

    private static Dictionary<string, string> KitHungarian()
    {
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(Kit, "Resources", "RaskStrings.hu.json")));
        return catalog.RootElement.EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!, StringComparer.Ordinal);
    }

    // Each source as written, and with what is not page text taken out: comments, attributes, the messages
    // of exceptions, and every literal that already goes through RaskStrings.
    private static IEnumerable<(string File, string Routed, string Unrouted)> KitSources() =>
        from path in Directory.EnumerateFiles(Kit, "*.cs", SearchOption.TopDirectoryOnly)
        let file = Path.GetFileName(path)
        where !file.StartsWith("UiIconPaths", StringComparison.Ordinal)
        let code = Comment().Replace(File.ReadAllText(path), string.Empty)
        select (file, code, CallSite().Replace(WithoutExceptions(code), "RaskStrings.Get("));

    private static string WithoutExceptions(string code)
    {
        for (var at = code.IndexOf("Exception(", StringComparison.Ordinal); at >= 0; at = code.IndexOf("Exception(", at, StringComparison.Ordinal))
        {
            var end = Closing(code, at + "Exception".Length);
            code = code.Remove(at, end - at);
        }

        return code;
    }

    // The index after the parenthesis that closes the one at `open`, with string literals stepped over.
    private static int Closing(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            switch (code[i])
            {
                case '"':
                    i = code.IndexOf('"', i + 1);
                    while (code[i - 1] == '\\')
                    {
                        i = code.IndexOf('"', i + 1);
                    }

                    break;
                case '(':
                    depth++;
                    break;
                case ')' when --depth == 0:
                    return i + 1;
            }
        }

        return code.Length;
    }

    [GeneratedRegex(@"^\s*(//|\[).*$", RegexOptions.Multiline)]
    private static partial Regex Comment();

    [GeneratedRegex("""RaskStrings\.Get\(\s*RaskString\.(\w+),\s*"((?:[^"\\]|\\.)*)"[,)]""")]
    private static partial Regex CallSite();

    [GeneratedRegex("""(?<![\w.])\$?"((?:&[a-z]+; )?[A-Z][a-z]+(?:(?:[ ,!?:…]|\.(?= |"))(?:[^"\\]|\\.)*)?)"(?!\s*=>)""")]
    private static partial Regex Prose();

    [GeneratedRegex("""(?:\.Aria\("(?:label|valuetext|description|roledescription|placeholder)",|\["(?:label|valuetext|placeholder|title)"\] =|\("(?:title|placeholder|alt|aria-label)",|\.(?:AriaLabel|Placeholder|Title|Alt|Content)\()\s*\$?"((?:[^"\\]|\\.)*[A-Za-z](?:[^"\\]|\\.)*)"\s*[,)\];]""")]
    private static partial Regex Sink();

    [GeneratedRegex(@"^\| `(\w+)` \| [^|]+ \| `(.+)` \|$", RegexOptions.Multiline)]
    private static partial Regex DocumentedRow();

    private sealed class AppCatalog(string culture, RaskString key, string text) : IRaskStringSource
    {
        public string? Get(RaskString asked, string cultureTag) =>
            asked == key && string.Equals(cultureTag, culture, StringComparison.Ordinal) ? text : null;
    }
}
