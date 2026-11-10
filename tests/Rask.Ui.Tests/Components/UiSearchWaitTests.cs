using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     How a search that asks the page waits, as Flux's backend search does (measured on fluxui.dev, 2026-11-10): it asks
///     150 ms after the last key, its field says it waits the moment the question leaves (<c>data-rask-loading-now</c>),
///     an answer to an older question is dropped, and its create row makes one option however often, and whenever, it is
///     pressed.
/// </summary>
/// <remarks>
///     The browser's part — the pause, the mark, the create row stepped over while the search is out — is played here by
///     the test; it is <c>FieldLoadingTests</c> and <c>CreateRowClickTests</c> on Jint, and <c>CreateRowWaitsTests</c>
///     (Server) and <c>UiKitSelectTests</c> (WebAssembly) in a browser. Here: the markup that asks for it, and what the
///     select does with presses and answers that arrive together, as on a host that runs handlers side by side.
/// </remarks>
[Collection(AskedSearchCollection.Name)]
public partial class UiSearchWaitTests : global::Rask.Core.RaskMarkup
{
    private const string Field = "[data-ui-select-search] input";
    private const string Create = "[data-ui-option-create]";

    private static readonly string[] Names = [.. Enumerable.Range(1, 30).Select(i => $"Partner {i:00}")];

    private readonly List<string> _asked = [];
    private readonly List<string> _made = [];
    private readonly Dictionary<string, TaskCompletionSource> _held = new(StringComparer.Ordinal);
    private TaskCompletionSource? _making;
    private int? _picked;
    private ICollection<int> _several = [];

    // A store that answers the best twenty, as Flux's does: at once, or once the test lets a held question go.
    private async Task<IEnumerable<Component?>> Find(string typed) =>
        (await Found(typed)).Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id)[p.Name]);

    private async Task<IEnumerable<(int Id, string Name)>> Found(string typed)
    {
        _asked.Add(typed);
        if (_held.TryGetValue(typed, out var gate))
        {
            await gate.Task;
        }

        return Names.Select((name, i) => (Id: i + 1, Name: name))
            .Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Take(20);
    }

    private async Task Make(string text)
    {
        if (_making is { } gate)
        {
            await gate.Task;
        }

        _made.Add(text);
    }

    private Component Select() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Search(Find)[
            Ui.SelectOptionCreate.OnClick(Make)["Create partner"]
        ];

    private Component Pills() =>
        Ui.Pillbox.Values(_several).OnChange(values => _several = values).Search(async typed =>
            (await Found(typed)).Select(p => Ui.PillboxOption.Key(p.Id).Value(p.Id)[p.Name]))[
            Ui.PillboxOptionCreate.OnClick(Make)["Create tag"]
        ];

    private TaskCompletionSource Hold(string typed)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _held[typed] = gate;
        return gate;
    }

    // A press or an answer that never comes fails the test instead of hanging it.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static string[] Rows(Page page) => [.. page.FindAll("[role=\"option\"]").Select(row => row.TextContent.Trim())];

    private static Task Pause(Page page, string text, string field = Field) =>
        page.On(field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    [Theory]
    [InlineData("zzz", 0)]
    [InlineData("Partner 07", 1)]
    [InlineData("Partner", 20)]
    public async Task An_answer_of_none_one_or_many_rows_is_drawn_whole_and_its_field_still_says_it_waits_at_once(string typed, int rows)
    {
        var page = Acting.Render(Select);

        await Pause(page, typed);

        Assert.Equal(rows, Rows(page).Length);
        Assert.Equal("150", page.Find(Field).Attribute("data-rask-debounce"));
        Assert.NotNull(page.Find(Field).Attribute("data-rask-loading-now"));
        if (rows == 0)
        {
            Assert.Equal("No results found", page.TextOf("[data-ui-listbox-empty]"));
        }
    }

    [Fact]
    public async Task Rapid_typing_whose_answers_come_back_out_of_order_shows_the_answer_to_the_last_question()
    {
        var page = Acting.Render(Select);
        var first = Hold("Partner 1");
        var second = Hold("Partner 2");

        var one = Pause(page, "Partner 1");
        var two = Pause(page, "Partner 2");
        var three = Pause(page, "Partner 3");
        await three.WaitAsync(Bound, TestContext.Current.CancellationToken);
        second.SetResult();
        await two.WaitAsync(Bound, TestContext.Current.CancellationToken);
        first.SetResult();
        await one.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(["", "Partner 1", "Partner 2", "Partner 3"], _asked);
        Assert.Equal(["Partner 30"], Rows(page));
    }

    [Fact]
    public async Task A_create_pressed_while_a_search_is_out_makes_nothing_and_one_pressed_after_the_answer_makes_one()
    {
        var page = Acting.Render(Select);
        await Pause(page, "Acme");
        var answer = Hold("Acme Ltd");

        var asking = Pause(page, "Acme Ltd");
        await page.On(Create).Click();
        var madeWhileAsked = _made.ToArray();
        answer.SetResult();
        await asking.WaitAsync(Bound, TestContext.Current.CancellationToken);
        await page.On(Create).Click();

        Assert.Empty(madeWhileAsked);
        Assert.Equal(["Acme Ltd"], _made);
    }

    [Fact]
    public async Task Two_presses_on_a_create_row_at_once_make_one_option()
    {
        var page = Acting.Render(Select);
        await Pause(page, "Acme");
        _making = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = page.On(Create).Click();
        var second = page.On(Create).Click();
        await second.WaitAsync(Bound, TestContext.Current.CancellationToken);
        _making.SetResult();
        await first.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(["Acme"], _made);
    }

    [Fact]
    public async Task A_create_pressed_as_a_search_starts_makes_one_option_and_the_search_is_still_answered()
    {
        var page = Acting.Render(Select);
        await Pause(page, "Acme");
        _making = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var making = page.On(Create).Click();
        var asking = Pause(page, "Partner 05");
        await asking.WaitAsync(Bound, TestContext.Current.CancellationToken);
        _making.SetResult();
        await making.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Equal(["Acme"], _made);
        Assert.Equal(["Partner 05"], Rows(page));
    }

    [Fact]
    public async Task A_pillbox_create_pressed_while_its_search_is_out_makes_nothing()
    {
        var page = Acting.Render(Pills);
        const string field = "[data-ui-pillbox-search] input";
        await Pause(page, "Acme", field);
        var answer = Hold("Acme Ltd");

        var asking = Pause(page, "Acme Ltd", field);
        await page.On(Create).Click();
        var madeWhileAsked = _made.ToArray();
        answer.SetResult();
        await asking.WaitAsync(Bound, TestContext.Current.CancellationToken);

        Assert.Empty(madeWhileAsked);
        Assert.NotNull(page.Find(field).Attribute("data-rask-loading-now"));
        Assert.NotNull(page.Find(Create).Attribute("data-rask-loading-now"));
    }
}
