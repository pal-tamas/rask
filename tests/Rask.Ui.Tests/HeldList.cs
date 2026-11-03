using System.Text.Json;
using Rask.Testing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests;

/// <summary>
///     What the browser does for a bound list that holds its pick until the next action (<c>held</c>,
///     <c>rask-list-held.ts</c>): the row pressed carries no handler, and its form value is written into the list's
///     one bound field, whose <c>change</c> the runtime sends ahead of the next action.
/// </summary>
internal static class HeldList
{
    private const string Value = "data-rask-list-value";

    /// <summary>Picks the row that says <paramref name="words" />, as the browser does.</summary>
    /// <param name="page">The page the list is on.</param>
    /// <param name="words">The row's words.</param>
    public static Task<string> Pick(Page page, string words)
    {
        var row = page.FindAll("[" + Value + "]").Single(row => string.Equals(row.TextContent, words, StringComparison.Ordinal));
        Assert.Null(row.Attribute("data-rask-on-click"));

        return Hold(page, row, row.Attribute(Value)!);
    }

    /// <summary>
    ///     Switches the row that says <paramref name="words" /> in a held list of several answers, as the browser
    ///     does: taken off when the list holds it, added after the rest when it does not.
    /// </summary>
    /// <param name="page">The page the list is on.</param>
    /// <param name="words">The row's words.</param>
    public static Task<string> Switch(Page page, string words)
    {
        var row = page.FindAll("[" + Value + "]").Single(row => string.Equals(row.TextContent, words, StringComparison.Ordinal));
        Assert.Null(row.Attribute("data-rask-on-click"));
        var value = row.Attribute(Value)!;
        var held = UiFormValues.Read(FieldOf(row).Attribute("value"));

        return Hold(page, row, held.Contains(value) ? [.. held.Where(one => one != value)] : [.. held, value]);
    }

    /// <summary>
    ///     Leaves a held list of several answers holding the rows that say <paramref name="words" />, in that order, as
    ///     the browser does once they are picked one after another.
    /// </summary>
    /// <param name="page">The page the list is on.</param>
    /// <param name="words">The rows' words.</param>
    public static Task<string> PickAll(Page page, params string[] words)
    {
        var rows = page.FindAll("[" + Value + "]");
        var picked = words.Select(said => rows.Single(row => string.Equals(row.TextContent, said, StringComparison.Ordinal))).ToList();
        Assert.All(picked, row => Assert.Null(row.Attribute("data-rask-on-click")));

        return Hold(page, picked[0], [.. picked.Select(row => row.Attribute(Value)!)]);
    }

    /// <summary>
    ///     Presses the cross of the pill that posts <paramref name="value" />, as the browser does: the list holds
    ///     <paramref name="left" /> (form values) after it.
    /// </summary>
    /// <param name="page">The page the pillbox is on.</param>
    /// <param name="value">The pill's form value.</param>
    /// <param name="left">The form values left.</param>
    public static Task<string> TakeOff(Page page, string value, params string[] left)
    {
        var cross = page.Find("[data-ui-pillbox-trigger] [data-value=\"" + value + "\"] [data-rask-list-remove]");
        Assert.Null(cross.Attribute("data-rask-on-click"));

        return Hold(page, cross, left);
    }

    /// <summary>Picks the first row of the first held list, as the browser does.</summary>
    /// <param name="page">The page the list is on.</param>
    public static Task<string> PickFirst(Page page)
    {
        var row = page.FindAll("[" + Value + "]")[0];

        return Hold(page, row, row.Attribute(Value)!);
    }

    /// <summary>Presses the clear button of the only held list, as the browser does.</summary>
    /// <param name="page">The page the list is on.</param>
    public static Task<string> Clear(Page page)
    {
        var clear = page.Find("[data-rask-list-clear]");
        Assert.Null(clear.Attribute("data-rask-on-click"));

        return Hold(page, clear);
    }

    // The list's one bound field: the first child of the list `inside` is in.
    private static HtmlNode FieldOf(HtmlNode inside)
    {
        var list = inside;
        while (list.Attribute("data-rask-list") is null)
        {
            list = list.Parent ?? throw new InvalidOperationException($"<{inside.Path()}> is in no list.");
        }

        return list.Children[0];
    }

    private static Task<string> Hold(Page page, HtmlNode inside, params string[] values)
    {
        var field = FieldOf(inside);
        Assert.Equal("action", field.Attribute("data-rask-bind-on"));

        return page.Invoke(field.Attribute("data-rask-on-change")!, "{\"value\":" + JsonSerializer.Serialize(UiFormValues.Write(values)) + "}");
    }
}
