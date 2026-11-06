using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Rask.Flux;

/// <summary>
///     Runs a function in the page. Its argument crosses as one JSON string, parsed there, so any .NET value or
///     <see cref="JsonNode" /> arrives as the plain object the function was written for; its answer comes back as a
///     <see cref="JsonNode" />, read the way the scripts read their results.
/// </summary>
public static class Eval
{
    /// <summary><c>page.evaluate(fn, arg)</c>.</summary>
    public static async Task<JsonNode?> Run(this IPage page, string fn, object? arg = null) =>
        ToNode(await page.EvaluateAsync<string?>($"async (a) => {{ const r = await ({fn})(a === undefined ? undefined : JSON.parse(a)); return r === undefined ? null : JSON.stringify(r); }}", Arg(arg)));

    /// <summary><c>handle.evaluate(fn, arg)</c>: <paramref name="fn" /> takes the element first.</summary>
    public static async Task<JsonNode?> Run(this IElementHandle handle, string fn, object? arg = null) =>
        ToNode(await handle.EvaluateAsync<string?>(Wrap(fn), Arg(arg)));

    /// <summary><c>handle.evaluate(fn, arg)</c> on a JS handle.</summary>
    public static async Task<JsonNode?> Run(this IJSHandle handle, string fn, object? arg = null) =>
        ToNode(await handle.EvaluateAsync<string?>(Wrap(fn), Arg(arg)));

    /// <summary><c>locator.evaluate(fn, arg)</c>.</summary>
    public static async Task<JsonNode?> Run(this ILocator locator, string fn, object? arg = null) =>
        ToNode(await locator.EvaluateAsync<string?>(Wrap(fn), Arg(arg)));

    /// <summary><c>locator.evaluateAll(fn, arg)</c>.</summary>
    public static async Task<JsonNode?> RunAll(this ILocator locator, string fn, object? arg = null) =>
        ToNode(await locator.EvaluateAllAsync<string?>(Wrap(fn), Arg(arg)));

    /// <summary>
    ///     What a function answered, which the page has put through JSON.stringify: so a result reads exactly as the file a
    ///     script wrote of it would (an undefined property left out, a number in JavaScript's digits), and Playwright's own
    ///     object encoding — which marks every object with a reference id — never meets the data. Null for undefined and null.
    /// </summary>
    public static JsonNode? ToNode(string? json) => json is null ? null : JsonNode.Parse(json);

    private static string Wrap(string fn) =>
        $"async (el, a) => {{ const r = await ({fn})(el, a === undefined ? undefined : JSON.parse(a)); return r === undefined ? null : JSON.stringify(r); }}";

    private static string? Arg(object? arg) => arg is null ? null : Js.Serialize(arg);
}
