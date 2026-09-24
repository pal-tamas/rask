using System.Net;
using System.Text;

namespace Rask.Site;

/// <summary>
/// The hero's code window: one feature, back to front, in two files — the aggregate that is the table,
/// and the page that queries it. A stateful Rask component; a tab click sets <c>_active</c> and
/// re-renders the pane, with no JavaScript.
/// </summary>
/// <remarks>
/// <para>
/// Not a <c>CodeSample</c>, which shows a file this app compiles beside the result it renders. This
/// site is a WebAssembly app with no database, so it cannot compile an aggregate — the sample is
/// the tutorial's own <c>Product</c> and <c>ProductsPage</c> (docs/tutorial/02-first-feature.md, whose
/// snippets the CLI build gate compiles), trimmed to what fits the window.
/// </para>
/// <para>
/// Every line stays under ~62 characters: the window is a 34rem track (see HomePage's hero grid) and
/// a line that does not fit makes the <c>&lt;pre&gt;</c> a horizontal scroller, which
/// SiteExampleTests.CodeSamples_FitTheirWindowsWithoutScrolling measures at every two-column width (on
/// the prerendered first tab — so keep the second tab's lines no longer than the Counter.cs sample's).
/// </para>
/// <para>
/// <c>.hero-code-tab</c> is a test contract: the browser suite switches files by it.
/// </para>
/// </remarks>
public sealed partial class HeroCode : Component
{
    private int _active;

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("hero-code overflow-hidden rounded-2xl border border-ui-line bg-ui-bg")[
            Div
                .Class("flex items-center gap-2 border-b border-ui-line bg-ui-well px-4 py-2")
                .Role("tablist")
                .Aria(new Dictionary<string, string?> { ["label"] = "Source file" })[
                Dot("#ff5f57"), Dot("#febc2e"), Dot("#28c840"),
                Div.Class("ml-2 flex min-w-0 gap-1")[Files.Select((file, index) => Tab(file.File, index))]
            ],
            Pre.Class("overflow-x-auto p-4 text-xs leading-relaxed").Role("tabpanel")[
                Code.Class("font-mono")[Raw.Value(Files[_active].Html)]
            ]
        ];

    private Component Tab(string file, int index) =>
        Button
            .Key(file)
            .Type("button")
            .Role("tab")
            .Aria(new Dictionary<string, string?> { ["selected"] = index == _active ? "true" : "false" })
            // min-h-9: a mono label is a small target, and these sit where a thumb reaches first.
            .Class("hero-code-tab inline-flex min-h-9 items-center rounded-md px-2 font-mono text-xs "
                   + (index == _active
                       ? "bg-ui-bg text-ui-ink"
                       : "text-ui-muted hover:text-ui-ink"))
            .OnClick(() => _active = index)[file];

    // A window-chrome dot. The colour is an inline style because these three are macOS's traffic lights,
    // not palette entries — putting them in the theme would invite something else to use them.
    private static Component Dot(string color) =>
        Span.Class("size-2.5 shrink-0 rounded-full").Style($"background:{color}");

    // ---- highlighting ----
    //
    // A deliberately small C# tokenizer for two fixed, trusted snippets, in the hero's own light palette
    // (the same utilities the Counter.cs window on this page uses). The site's ColorCode highlighter
    // colours for the dark code pane, and its classes are only styled inside .sample-code.

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "sealed", "partial", "class", "override", "readonly", "get",
        "set", "return", "var", "string", "decimal", "bool", "true", "false", "new",
    };

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "Product", "Aggregate", "Guid", "Required", "MaxLength", "Range", "Route", "ProductsPage",
        "Component", "IQueryable", "ProductRead", "QueryClient", "QueryKey", "Ui",
    };

    // Declared after Keywords and Types on purpose: static initializers run in textual order, and
    // Highlight reads both sets.
    private static readonly (string File, string Html)[] Files =
    [
        ("Product.cs", Highlight(
            """
            // A table, its columns and a generated read face.
            // No DbSet, no configuration class, no registration.
            public sealed class Product : Aggregate<Guid>
            {
                [Required, MaxLength(200)]
                public string Name { get; private set; } = "";

                [Range(0, 1_000_000)]
                public decimal Price { get; private set; }

                public bool InStock { get; private set; } = true;
            }
            """)),
        ("ProductsPage.cs", Highlight(
            """
            [Route("/products")]
            public sealed partial class ProductsPage : Component
            {
                private readonly IQueryable<ProductRead> _products =
                    Product.Read.OrderBy(p => p.Name).AsQueryable();

                protected override Component? Render()
                {
                    // Cached, and refetched after any Product write.
                    var count = QueryClient.Query(QueryKey.For<Product>(),
                        ct => Product.Read.CountAsync(ct));

                    return
                    [
                        Ui.Header.Heading($"Products ({count.Data})"),
                        Ui.DataGrid.Data(_products).RowKey(p => p.Id)
                            .Label("Products")[c => [
                                c.Field(p => p.Name).Sortable(true),
                                c.Field(p => p.Price).Sortable(true),
                            ]]
                    ];
                }
            }
            """)),
    ];

    private static string Highlight(string source)
    {
        var html = new StringBuilder(source.Length * 2);
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var end = source.IndexOf('\n', i);
                end = end < 0 ? source.Length : end;
                Token(html, "text-ui-muted", source[i..end]);
                i = end;
            }
            else if (c == '"' || (c == '$' && i + 1 < source.Length && source[i + 1] == '"'))
            {
                var open = c == '$' ? i + 1 : i;
                var close = source.IndexOf('"', open + 1);
                close = close < 0 ? source.Length - 1 : close;
                Token(html, "text-amber-700", source[i..(close + 1)]);
                i = close + 1;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_'))
                {
                    i++;
                }

                var word = source[start..i];
                if (Keywords.Contains(word))
                {
                    Token(html, "text-ui-brand-ink", word);
                }
                else if (Types.Contains(word))
                {
                    Token(html, "text-ui-ok-ink", word);
                }
                else
                {
                    html.Append(WebUtility.HtmlEncode(word));
                }
            }
            else
            {
                html.Append(WebUtility.HtmlEncode(c.ToString()));
                i++;
            }
        }

        return html.ToString();
    }

    private static void Token(StringBuilder html, string cls, string text) =>
        html.Append("<span class=\"").Append(cls).Append("\">").Append(WebUtility.HtmlEncode(text)).Append("</span>");
}
