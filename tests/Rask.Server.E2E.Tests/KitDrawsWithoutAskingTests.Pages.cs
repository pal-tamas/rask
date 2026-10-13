using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Server.E2E.Tests;

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for what only shows something.</summary>
public sealed class KitDisplayDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitDisplayFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for the controls that keep a value.</summary>
public sealed class KitFieldDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitFieldFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for what opens over the page.</summary>
public sealed class KitOverlayDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitOverlayFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for what shows one view of several.</summary>
public sealed class KitNavigationDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitNavigationFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for the sidebar layout.</summary>
public sealed class KitSidebarDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitSidebarFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for the pickers.</summary>
public sealed class KitPickerDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitPickerFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for the select family.</summary>
public sealed class KitSelectDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitSelectFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for the chart.</summary>
public sealed class KitChartDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitChartFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for the editor.</summary>
public sealed class KitEditorDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitEditorFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary><see cref="KitDrawsWithoutAskingTests" /> for the kit's own components.</summary>
public sealed class KitOwnDrawsWithoutAskingTests(PlaywrightFixture playwright) : KitDrawsWithoutAskingTests(playwright), IClassFixture<PlaywrightFixture>
{
    public static TheoryData<string> Components => Drawn<KitOwnFramePage>();

    [Theory]
    [MemberData(nameof(Components))]
    public Task A_gesture_that_only_changes_what_is_drawn_sends_the_server_nothing(string component) => HoldAsync(component);
}

/// <summary>The budget lists themselves: every row names a gesture that is counted, once, on a page that is walked.</summary>
public sealed class KitFrameBudgetTests
{
    private static readonly Type[] Walked =
    [
        typeof(KitDisplayFramePage),
        typeof(KitFieldFramePage),
        typeof(KitOverlayFramePage),
        typeof(KitNavigationFramePage),
        typeof(KitSidebarFramePage),
        typeof(KitPickerFramePage),
        typeof(KitSelectFramePage),
        typeof(KitChartFramePage),
        typeof(KitEditorFramePage),
        typeof(KitOwnFramePage),
    ];

    [Fact]
    public void Every_budget_names_a_gesture_that_is_counted_and_is_in_one_list_only()
    {
        var strays = KitDrawsWithoutAskingTests.Strays();

        var doubles = KitDrawsWithoutAskingTests.Doubles();

        Assert.Empty(strays);
        Assert.Empty(doubles);
    }

    [Fact]
    public void Every_page_of_components_has_a_class_that_walks_it()
    {
        var pages = KitDrawsWithoutAskingTests.Pages();

        var unwalked = pages.Except(Walked).ToList();

        Assert.Empty(unwalked);
    }
}
