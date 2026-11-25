using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.TestSupport;

namespace Rask.UiTests.Live;

/// <summary>
///     A kit button inside a kit card, whose click adds to a list the component around both owns, through a lambda that
///     captures no <c>this</c> — only that list and a delegate of the component's own: the component that wrote the
///     button draws the row it added.
/// </summary>
public sealed partial class HandlerInsideACardTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_kit_button_in_a_card_that_adds_to_a_local_list_redraws_the_component_that_wrote_it(bool insideAnOuterCard)
    {
        var view = new StubComponent(() => insideAnOuterCard ? CardRowsPage : CardRowsEditor);
        var before = view.RenderAsLiveRoot();
        var add = Regex.Match(before, "data-rask-on-click=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        using var payload = JsonDocument.Parse("{}");

        await view.TryInvokeHandlerAsync(add, payload.RootElement);
        var after = view.RenderAsLiveRoot();

        Assert.Contains("rows=0", before, StringComparison.Ordinal);
        Assert.Contains("rows=1", after, StringComparison.Ordinal);
        Assert.Contains("row-1", after, StringComparison.Ordinal);
    }
}

// A page holding the editor inside a card of its own.
internal sealed partial class CardRowsPage : Component
{
    protected override Component? Render() => Ui.Card[CardRowsEditor];
}

// The editor: its list drawn inside a card, the button's handler closing over the list and a delegate, not `this`.
internal sealed partial class CardRowsEditor : Component
{
    private readonly List<string> _rows = [];

    protected override Component? Render() => Div[Ui.Card[Rows(_rows, Named)]];

    // The rows and the button, as an editor's helper draws them: the lambdas of one method share one closure, so the
    // button's handler holds the list AND the delegate a row is drawn with — a method of this component's own, captured
    // as a delegate rather than as `this`.
    private static Component Rows(List<string> rows, Func<string, string> name) =>
        Div[
            Span[$"rows={rows.Count}"],
            rows.Select(row => Span.Key(row)[name(row)]),
            Ui.Button.OnClick(() => rows.Add($"row-{rows.Count + 1}"))["add"]
        ];

    private string Named(string row) => row + (_rows.Count > 0 ? "" : "!");
}
