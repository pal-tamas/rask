using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     What one page appended to a paging <c>.Choices((typed, page) =&gt; …)</c> costs on the wire (#1354): the scroll that
///     reaches the end of the rows here sends the rows the box now shows, as any scroll of a windowed list does, and the
///     page itself only as the taller spacer and the new count — its rows are drawn when a scroll reaches them.
/// </summary>
/// <remarks>The budget is the exact count, so a byte more is a red. Lower it when a change takes bytes off.</remarks>
[Collection(Components.AskedSearchCollection.Name)]
public partial class PagedChoicesBytesTests(ITestOutputHelper output) : global::Rask.Core.RaskMarkup
{
    private const int AppendBudget = 18_388;

    private int? _picked;

    private static string NameOf(int id) => "Product " + id.ToString("000", CultureInfo.InvariantCulture);

    private static Task<IReadOnlyList<Ui.Choice<int>>> Store(string typed, Ui.ChoicePage page) =>
        Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
            [.. Enumerable.Range(1, 500).Select(id => new Ui.Choice<int>(id, NameOf(id))).Skip(page.Skip).Take(page.Take)]);

    // The same 500 rows held whole: its scroll is the one a paging list's is measured against.
    private static Task<IReadOnlyList<Ui.Choice<int>>> Whole(string typed) =>
        Task.FromResult<IReadOnlyList<Ui.Choice<int>>>([.. Enumerable.Range(1, 500).Select(id => new Ui.Choice<int>(id, NameOf(id)))]);

    // Counted as the first select of a process writes it: ids are numbered by how many the process drew before.
    private static int Bytes(string text) => Encoding.UTF8.GetByteCount(Numbered().Replace(text, "${name}-1"));

    [GeneratedRegex(@"(?<name>uisel|f-field)-\d+")]
    private static partial Regex Numbered();

    private static Task Scroll(Page page, int row) =>
        page.On("[data-rask-list-window]").Raise(
            "scroll", "{\"target\":{\"scrollTop\":" + (row * 32).ToString(CultureInfo.InvariantCulture) + ",\"clientHeight\":320}}");

    [Fact]
    public async Task A_scroll_that_appends_a_page_sends_no_more_than_the_same_scroll_of_rows_held_whole()
    {
        var held = Page.Render(() => Ui.Select.Value(_picked).OnChange(value => _picked = value).Choices(Whole));
        await Scroll(held, 30);
        var heldFrame = Bytes(DiffGuardAttribute.Frames()[^1]);
        var paged = Page.Render(() => Ui.Select.Value(_picked).OnChange(value => _picked = value).Choices(Store));

        await Scroll(paged, 30);

        var frame = Bytes(DiffGuardAttribute.Frames()[^1]);
        output.WriteLine("scroll of rows held whole bytes: " + heldFrame.ToString("N0", CultureInfo.InvariantCulture));
        output.WriteLine("scroll that appends a page bytes: " + frame.ToString("N0", CultureInfo.InvariantCulture));
        Assert.Contains("32 1 100", paged.Html, StringComparison.Ordinal);
        Assert.InRange(frame, 1, AppendBudget);
        Assert.True(frame <= heldFrame, $"{frame} bytes paged against {heldFrame} held whole.");
    }
}
