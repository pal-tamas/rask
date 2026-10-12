using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests;

/// <summary>
///     A bound control beside the one thing its value travels with: an action. A control the kit draws writes
///     what was chosen with the next handler that is not its own, so a test of what it writes presses this
///     button and reads the model after it, as a page reads it in its save.
/// </summary>
internal sealed partial class Acting : RaskMarkup
{
    private const string Button = "act-on-what-was-chosen";

    /// <summary>Renders <paramref name="content" /> with a button after it, so no selector of the test meets the button first.</summary>
    /// <param name="content">What the test is about.</param>
    public static Page Render(Func<Component?> content) =>
        Page.Render(() => Fragment[content(), Markup.Button.Id(Button).OnClick(static () => { })["Act"]]);

    /// <inheritdoc cref="Render(Func{Component})" />
    public static Page Render(Component content) => Render(() => content);

    /// <summary>Presses the button: what was chosen is written, its rules run and <c>AfterBind</c> is called.</summary>
    /// <param name="page">A page <see cref="Render" /> made.</param>
    public static Task<string> Act(Page page) => page.On("#" + Button).Click();
}
