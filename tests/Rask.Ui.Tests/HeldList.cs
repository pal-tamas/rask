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

    private static Task<string> Hold(Page page, HtmlNode inside, params string[] values)
    {
        var list = inside;
        while (list.Attribute("data-rask-list") is null)
        {
            list = list.Parent ?? throw new InvalidOperationException($"<{inside.Path()}> is in no list.");
        }

        var field = list.Children[0];
        Assert.Equal("action", field.Attribute("data-rask-bind-on"));

        return page.Invoke(field.Attribute("data-rask-on-change")!, "{\"value\":" + JsonSerializer.Serialize(UiFormValues.Write(values)) + "}");
    }
}
