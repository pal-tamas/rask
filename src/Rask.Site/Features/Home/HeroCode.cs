using System.Net;
using System.Text;

namespace Rask.Site;

/// <summary>
/// The hero's code window: the one way a Rask app is written, in three files — a model (value objects,
/// a typed id and an aggregate whose methods are its writes), the form over one of its operations, and
/// the test that proves its rule on SQLite in memory. A stateful Rask component; a tab click sets
/// <c>_active</c> and re-renders the pane, with no JavaScript.
/// </summary>
/// <remarks>
/// <para>
/// Not a <c>CodeSample</c>, which shows a file this app compiles beside the result it renders. This
/// site is a WebAssembly app with no database, so it cannot compile an aggregate. The three files are
/// compiled — and the test run — in tests/Rask.Testing.Database.Tests/Hero instead, and
/// <c>HeroCodeSourceTests</c> holds <see cref="Sources" /> to them character for character, so the front
/// page cannot show an API that does not build.
/// </para>
/// <para>
/// Every line stays under ~62 characters: the window is a 34rem track (see HomePage's hero grid) and
/// a line that does not fit makes the <c>&lt;pre&gt;</c> a horizontal scroller, which
/// SiteExampleTests.CodeSamples_FitTheirWindowsWithoutScrolling measures at every two-column width (on
/// the prerendered first tab — so keep the other tabs' lines no longer than the Counter.cs sample's).
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
        Div.Class("hero-code overflow-hidden rounded-2xl border border-zinc-200 dark:border-white/10 bg-white dark:bg-zinc-800")[
            Div
                .Class("flex items-center gap-2 border-b border-zinc-200 dark:border-white/10 bg-zinc-50 dark:bg-zinc-900 px-4 py-2")
                .Role("tablist")
                .Aria("label", "Source file")[
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
            .Type(ButtonType.Button)
            .Role("tab")
            .Aria("selected", index == _active ? "true" : "false")
            // min-h-9: a mono label is a small target, and these sit where a thumb reaches first.
            .Class("hero-code-tab inline-flex min-h-9 items-center rounded-md px-2 font-mono text-xs "
                   + (index == _active
                       ? "bg-white dark:bg-zinc-800 text-zinc-800 dark:text-white"
                       : "text-zinc-500 dark:text-white/70 hover:text-zinc-800 dark:hover:text-white"))
            .OnClick(() => _active = index)[file];

    // A window-chrome dot. The colour is an inline style because these three are macOS's traffic lights,
    // not palette entries — putting them in the theme would invite something else to use them.
    private static Rask.Core.HTMLSpanElement Dot(string color) =>
        Span.Class("size-2.5 shrink-0 rounded-full").Style($"background:{color}");

    // ---- highlighting ----
    //
    // A deliberately small C# tokenizer for three fixed, trusted snippets, in the hero's own light palette
    // (the same utilities the Counter.cs window on this page uses). The site's ColorCode highlighter
    // colours for the dark code pane, and its classes are only styled inside .sample-code.

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "public", "private", "protected", "sealed", "partial", "class", "record", "struct", "override",
        "readonly", "get", "set", "return", "var", "string", "decimal", "bool", "void", "true", "false",
        "new", "null", "async", "await", "using",
    };

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "Product", "ProductId", "ProductName", "UnitPrice", "Text", "Number", "Aggregate", "Guid",
        "Timestamps", "Versioned", "Deletable", "Refuse", "PriceForm", "Component", "Form", "Ui",
        "ProductTests", "Fact", "Task", "TestDatabase", "Test",
    };

    /// <summary>
    /// The three files the window shows, verbatim: everything after the namespace line of
    /// tests/Rask.Testing.Database.Tests/Hero/*.cs, where they are compiled and the test is run.
    /// <c>HeroCodeSourceTests</c> fails when one drifts from its file.
    /// </summary>
    internal static readonly string[] Sources =
    [
        """
        // A value object carries its rule; a typed id is one line.
        public readonly partial record struct ProductId(Guid Value);
        public sealed partial record ProductName(string Value)
            : Text(Value, MaxLength: 200);
        public sealed partial record UnitPrice(decimal Value)
            : Number(Value, Min: 0, Scale: 2);

        // The aggregate is the table, and each method is one write.
        public sealed partial class Product
            : Aggregate<ProductId, Timestamps, Versioned, Deletable>
        {
            public ProductName Name { get; private set; } = null!;
            public UnitPrice Price { get; private set; } = null!;
            public bool Discontinued { get; private set; }

            // A factory: Product.Add(…) saves what it makes.
            private static Product Add(ProductName n, UnitPrice p) =>
                new() { Id = ProductId.New(), Name = n, Price = p };

            public void Reprice(UnitPrice price)
            {
                Refuse.When(Discontinued, () => Price,
                    "A discontinued product keeps its price.");
                Price = price;
            }

            public void Discontinue() => Discontinued = true;
        }
        """,
        """
        // A form over the operation: its field is Reprice's price,
        // checked by UnitPrice and the Refuse as it is typed, then
        // saved through Product.Reprice(id, version, price).
        public sealed partial class PriceForm : Component
        {
            public ProductId Id { get; set; }

            protected override Component? Render() =>
                Form.For(Product.Reprice, Id)[f => [
                    Ui.Field.Bind(() => f.Price).Label("Price"),
                    Ui.Button.Primary.Submit["Save"],
                ]];
        }
        """,
        """
        // SQLite in memory: a database per test, run side by side.
        public sealed class ProductTests
        {
            [Fact]
            public async Task A_discontinued_product_keeps_its_price()
            {
                await using var db = await TestDatabase.Start();
                var id = await Product.Add(Test.Make<ProductName>(),
                    UnitPrice.Create(9.90m));
                var product = (await Product.Find(id))!;
                product.Discontinue();
                await product.Save();

                var cheaper = UnitPrice.Create(5m);
                var refused = await Test.Refuses(() =>
                    Product.Reprice(id, product.Version, cheaper));

                refused.Only("Price");
            }
        }
        """,
    ];

    // Declared after Keywords and Types on purpose: static initializers run in textual order, and
    // Highlight reads both sets.
    private static readonly (string File, string Html)[] Files =
    [
        ("Product.cs", Highlight(Sources[0])),
        ("PriceForm.cs", Highlight(Sources[1])),
        ("ProductTests.cs", Highlight(Sources[2])),
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
                Token(html, "text-zinc-500 dark:text-white/70", source[i..end]);
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
                    Token(html, "text-accent-content", word);
                }
                else if (Types.Contains(word))
                {
                    Token(html, "text-green-700 dark:text-green-400", word);
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
