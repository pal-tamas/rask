using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A search that asks the page, and its create row, on a Server page — as Flux's backend search behaves, measured on
///     fluxui.dev (2026-11-10): the field says it waits the moment the question leaves; while it is out the create row is
///     greyed, <c>aria-disabled</c>, stepped over by the keys and refuses a click from anywhere; pressed, it spins and is
///     busy, refuses a second press and holds the list open until its option is made. The empty list says "Loading..."
///     meanwhile (the owner's departure from Flux's "No results found").
/// </summary>
/// <remarks>
///     The store and the create answer when the test says (<see cref="CreateWaitsGate" />), so every state is held as
///     long as it is asserted: nothing here races a timer.
/// </remarks>
public sealed class CreateRowWaitsTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Field = "[data-ui-select-search] input";
    private const string Create = "[data-ui-option-create]";
    private static readonly System.Text.RegularExpressions.Regex Anything = new(".*");

    public static TheoryData<int, int> Screens => new() { { 1280, 800 }, { 390, 844 } };

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task A_create_row_waits_for_the_search_and_spins_until_its_option_is_made(int width, int height)
    {
        var gate = new CreateWaitsGate();
        await using var session = await Open(gate, new() { ViewportSize = new() { Width = width, Height = height }, HasTouch = width < 640 });
        var page = session.Page;
        var select = page.Locator("[data-ui-select]", new() { Has = page.Locator("#partner") });
        var field = select.Locator(Field);
        var create = select.Locator(Create);
        await page.Locator("#partner").ClickAsync();
        await field.PressSequentiallyAsync("Acme");
        await Expect(create).ToBeVisibleAsync();
        await Expect(field).Not.ToHaveAttributeAsync("data-loading", Anything);
        await Expect(create).Not.ToHaveAttributeAsync("aria-disabled", Anything);

        // The question goes out and is held: the field waits, the row is off, the empty list says it is loading.
        gate.HoldSearches();
        await field.PressSequentiallyAsync(" Ltd");
        await Expect(field).ToHaveAttributeAsync("data-loading", "");
        await Expect(create).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.Null(await field.GetAttributeAsync("aria-busy"));
        Assert.Equal("none", await create.EvaluateAsync<string>("el => getComputedStyle(el).pointerEvents"));
        Assert.Equal("\"Loading...\"", await select.Locator("[data-ui-listbox-empty]").EvaluateAsync<string>("el => getComputedStyle(el, '::before').content"));
        // A click that reaches it anyway is refused, as the pointer is.
        await create.DispatchEventAsync("click");
        Assert.True(await session.ShownAsync("[data-ui-select] [popover]"));
        gate.ReleaseSearches();
        await Expect(field).Not.ToHaveAttributeAsync("data-loading", Anything);
        await Expect(create).Not.ToHaveAttributeAsync("aria-disabled", Anything);
        await Expect(page.Locator("#state")).ToHaveTextAsync("Partner: none. Made: 0.");

        // Pressed: it spins at once and is busy, a second press is dropped, and the list stays open until it is made.
        gate.HoldCreates();
        await create.ClickAsync();
        await Expect(create).ToHaveAttributeAsync("data-loading", "");
        await Expect(create).ToHaveAttributeAsync("aria-busy", "true");
        await Expect(create.Locator("svg").Last).ToBeVisibleAsync();
        await create.DispatchEventAsync("click");
        Assert.True(await session.ShownAsync("[data-ui-select] [popover]"));
        gate.ReleaseCreates();

        await Expect(page.Locator("#state")).ToHaveTextAsync("Partner: Acme Ltd. Made: 1.");
        await Expect(page.Locator("#partner")).ToContainTextAsync("Acme Ltd");
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-ui-select] [popover]').matches(':popover-open')");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task From_the_keyboard_alone_the_create_row_is_stepped_over_while_the_search_is_out_and_made_once_after()
    {
        var gate = new CreateWaitsGate();
        await using var session = await Open(gate, null);
        var page = session.Page;
        var select = page.Locator("[data-ui-select]", new() { Has = page.Locator("#partner") });
        var field = select.Locator(Field);
        var create = select.Locator(Create);
        await page.Locator("#partner").FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(field).ToBeFocusedAsync();
        await page.Keyboard.TypeAsync("Al");
        await Expect(create).ToBeVisibleAsync();
        await Expect(field).Not.ToHaveAttributeAsync("data-loading", Anything);
        var createId = await create.GetAttributeAsync("id");

        gate.HoldSearches();
        await page.Keyboard.TypeAsync("p");
        await Expect(create).ToHaveAttributeAsync("aria-disabled", "true");
        for (var i = 0; i < 4; i++)
        {
            await page.Keyboard.PressAsync("ArrowDown");
        }

        // The arrows stop on the last row on offer: the create row after it is stepped over.
        var last = await select.Locator("[data-ui-option]", new() { HasTextString = "Baltic Shipping" }).GetAttributeAsync("id");
        Assert.Equal(last, await field.GetAttributeAsync("aria-activedescendant"));
        Assert.NotEqual(createId, last);
        gate.ReleaseSearches();
        await Expect(create).Not.ToHaveAttributeAsync("aria-disabled", Anything);
        // On offer again: the arrows reach it. Its id is its place among the rows, which the answer has moved.
        var offered = await create.GetAttributeAsync("id");
        for (var i = 0; i < 3 && await field.GetAttributeAsync("aria-activedescendant") != offered; i++)
        {
            await page.Keyboard.PressAsync("ArrowDown");
        }

        await Expect(field).ToHaveAttributeAsync("aria-activedescendant", offered!);

        gate.HoldCreates();
        await page.Keyboard.PressAsync("Enter");
        await Expect(create).ToHaveAttributeAsync("aria-busy", "true");
        await page.Keyboard.PressAsync("Enter");
        gate.ReleaseCreates();

        await Expect(page.Locator("#state")).ToHaveTextAsync("Partner: Alp. Made: 1.");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task A_pillbox_create_row_waits_for_its_search_and_spins_until_its_tag_is_made()
    {
        var gate = new CreateWaitsGate();
        await using var session = await Open(gate, null);
        var page = session.Page;
        var scope = page.Locator("[data-ui-pillbox]");
        var field = scope.Locator("[data-ui-pillbox-search] input");
        var create = scope.Locator(Create);
        await scope.Locator("[data-ui-pillbox-trigger]").ClickAsync();
        await field.PressSequentiallyAsync("Urgent");
        await Expect(create).ToBeVisibleAsync();

        gate.HoldSearches();
        await field.PressSequentiallyAsync("!");
        await Expect(field).ToHaveAttributeAsync("data-loading", "");
        await Expect(create).ToHaveAttributeAsync("aria-disabled", "true");
        gate.ReleaseSearches();
        await Expect(create).Not.ToHaveAttributeAsync("aria-disabled", Anything);
        gate.HoldCreates();
        await create.ClickAsync();
        await Expect(create).ToHaveAttributeAsync("aria-busy", "true");
        gate.ReleaseCreates();

        await Expect(page.Locator("#tags")).ToHaveTextAsync("Tags: Urgent!.");
        Assert.Empty(session.PageErrors);
    }

    private Task<HookSession> Open(CreateWaitsGate gate, BrowserNewContextOptions? options) =>
        HookSession.OpenAsync<CreateWaitsApp>(playwright, options, path: "/create-waits", services: s => s.AddSingleton(gate));
}

/// <summary>When the page's store and its create answer: at once, or once the test lets them go.</summary>
public sealed class CreateWaitsGate
{
    private TaskCompletionSource? _searches;
    private TaskCompletionSource? _creates;

    public Task Searched => Volatile.Read(ref _searches)?.Task ?? Task.CompletedTask;

    public Task Created => Volatile.Read(ref _creates)?.Task ?? Task.CompletedTask;

    public void HoldSearches() => Volatile.Write(ref _searches, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public void ReleaseSearches() => Interlocked.Exchange(ref _searches, null)?.TrySetResult();

    public void HoldCreates() => Volatile.Write(ref _creates, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public void ReleaseCreates() => Interlocked.Exchange(ref _creates, null)?.TrySetResult();
}

/// <summary>The app: its router.</summary>
public sealed partial class CreateWaitsApp : Component
{
    protected override Component? HeadAssets => [Markup.Title["create waits"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Router;
}

/// <summary>A select and a pillbox whose searches the page answers from a store, each with a create row.</summary>
[Route("/create-waits")]
public sealed partial class CreateWaitsPage(CreateWaitsGate gate) : Component
{
    private readonly List<(int Id, string Name)> _partners = [(1, "Alpine Freight"), (2, "Atlas Haulage"), (3, "Baltic Shipping")];
    private readonly List<(int Id, string Name)> _tags = [(1, "Billing"), (2, "Support")];
    private int? _picked;
    private ICollection<int> _chosen = [];
    private int _made;

    protected override Component? Render() =>
        Main[
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Id("partner").Placeholder("Choose a partner...").Search(Find)[
                Ui.SelectOptionCreate.OnClick(Make)["Create partner"]
            ],
            P.Id("state")[$"Partner: {_partners.FirstOrDefault(p => p.Id == _picked).Name ?? "none"}. Made: {_made}."],
            Ui.Pillbox.Values(_chosen).OnChange(values => _chosen = values).Placeholder("Choose tags...").Search(FindTags)[
                Ui.PillboxOptionCreate.OnClick(MakeTag)["Create tag"]
            ],
            P.Id("tags")[$"Tags: {string.Join(", ", _tags.Where(t => _chosen.Contains(t.Id)).Select(t => t.Name))}."]
        ];

    // The best twenty for what was typed, once the store answers.
    private async Task<IEnumerable<Component?>> Find(string typed)
    {
        await gate.Searched;
        return _partners.Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Take(20)
            .Select(p => Ui.SelectOption.Key(p.Id).Value(p.Id)[p.Name]);
    }

    private async Task Make(string name)
    {
        await gate.Created;
        var id = _partners.Max(p => p.Id) + 1;
        _partners.Add((id, name.Trim()));
        _picked = id;
        _made++;
    }

    private async Task<IEnumerable<Component?>> FindTags(string typed)
    {
        await gate.Searched;
        return _tags.Where(t => t.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)).Take(20)
            .Select(t => Ui.PillboxOption.Key(t.Id).Value(t.Id)[t.Name]);
    }

    private async Task MakeTag(string name)
    {
        await gate.Created;
        var id = _tags.Max(t => t.Id) + 1;
        _tags.Add((id, name.Trim()));
        _chosen = [.. _chosen, id];
    }
}
