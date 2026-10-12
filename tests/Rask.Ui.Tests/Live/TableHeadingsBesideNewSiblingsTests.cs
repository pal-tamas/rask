using System.Text.RegularExpressions;
using Rask.Core;
using Rask.TestSupport;

namespace Rask.UiTests.Live;

/// <summary>
///     A table's headings keep their own words when kit components appear ahead of the table.
/// </summary>
/// <remarks>
///     Reported from an app: ticking a row brought a "3 chosen" text and a "Clear" button into the toolbar
///     above the table, and the nine headings then showed one another's words — in the right, keyed
///     <c>&lt;th&gt;</c>s, in the whole page as well as in a diff.
/// </remarks>
public sealed partial class TableHeadingsBesideNewSiblingsTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Expected = ["h0,h1,h2,h3,h4,h5,h6,h7,h8"];

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Headings_keep_their_own_words_when_a_text_and_a_button_appear_in_the_toolbar_above(bool keyed, bool plainFirst)
    {
        var chosen = false;
        var view = new StubComponent(() => HeadingsPage.Chosen(chosen).Keyed(keyed).PlainFirst(plainFirst));
        var before = Headings(view.RenderAsLiveRoot());

        chosen = true;
        var ticked = Headings(view.RenderAsLiveRoot());
        var again = Headings(view.RenderAsLiveRoot());
        chosen = false;
        var cleared = Headings(view.RenderAsLiveRoot());

        Assert.Equal(Expected, before);
        Assert.Equal(Expected, ticked);
        Assert.Equal(Expected, again);
        Assert.Equal(Expected, cleared);
    }

    // The hard case: what appears ahead is built from the very types a heading is built from.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Headings_keep_their_own_words_when_another_heading_appears_ahead_of_them(bool keyed)
    {
        var chosen = false;
        var view = new StubComponent(() => HeadingsPage.Chosen(chosen).Keyed(keyed).PlainFirst(true).Extra(true));
        var before = Headings(view.RenderAsLiveRoot());

        chosen = true;
        var ticked = Headings(view.RenderAsLiveRoot());
        var again = Headings(view.RenderAsLiveRoot());
        chosen = false;
        var cleared = Headings(view.RenderAsLiveRoot());

        Assert.Equal(Expected, before);
        Assert.Equal(["extra," + Expected[0]], ticked);
        Assert.Equal(["extra," + Expected[0]], again);
        Assert.Equal(Expected, cleared);
    }

    private static string[] Headings(string html) => [string.Join(",", HeadingsOf(html))];

    private static string[] HeadingsOf(string html) =>
        [.. HeadingPattern().Matches(html).Select(th => TagPattern().Replace(th.Groups[1].Value, ""))];

    [GeneratedRegex("<th[^>]*>(.*?)</th>", RegexOptions.Singleline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}

/// <summary>The reported page's shape: a toolbar whose last two children come and go, then a table of nine headings.</summary>
internal sealed partial class HeadingsPage : Component
{
    public required bool Chosen { get; set; }

    public required bool Keyed { get; set; }

    public required bool PlainFirst { get; set; }

    /// <summary>Whether what appears is a table with a heading of its own, rather than a text and a button.</summary>
    public bool? Extra { get; set; }

    protected override Component? Render() =>
    [
        Div[H1["Stock"]],
        Div.Key("actions")[
            Ui.Button.Key("move")["Move"],
            Ui.Button.Key("merge")["Merge"],
            Chosen ? Ui.Text.Key("count")["1 chosen"] : null,
            Chosen ? Ui.Button.Key("none").Ghost.Sm["Clear"] : null
        ],
        Chosen && Extra is true
            ? Ui.Table.Key("extra")[Ui.TableColumns[Ui.TableColumn["extra"]], Ui.TableRows[Ui.TableRow.Key("one")[Ui.TableCell["cell"]]]]
            : null,
        Ui.Table.Key("rows")[
            Ui.TableColumns[
                Column(0, PlainFirst ? "h0" : Span.Class("sr-only")["h0"]),
                Enumerable.Range(1, 8).Select(index => Column(index, $"h{index}"))
            ],
            Ui.TableRows[Ui.TableRow.Key("row")[Ui.TableCell["cell"]]]
        ]
    ];

    private Component Column(int index, Component label) =>
        Keyed ? Ui.TableColumn.Key($"k{index}")[label] : Ui.TableColumn[label];
}
