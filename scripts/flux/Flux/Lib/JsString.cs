using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rask.Flux;

/// <summary>
///     JavaScript's <c>String(value)</c> of a value a page answered, for a report that prints it as the scripts did:
///     a string as itself, a number in JavaScript's digits, an array joined by commas, a missing value as
///     <c>undefined</c> — and an object as <c>[object Object]</c>, which no report should print.
/// </summary>
public static class JsString
{
    /// <summary><c>String(node)</c>; <paramref name="node" /> null is JSON's <c>null</c>.</summary>
    public static string Of(JsonNode? node) => node switch
    {
        null => "null",
        JsonArray array => string.Join(',', array.Select(item => item is null ? "" : Of(item))),
        JsonObject => "[object Object]",
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => Js.Num(Js.Number(value)),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "null",
        },
        _ => "undefined",
    };

    /// <summary><c>String(obj[key])</c>: <c>undefined</c> where <paramref name="obj" /> has no such key.</summary>
    public static string Of(JsonObject? obj, string key) => obj is not null && obj.TryGetPropertyValue(key, out var value) ? Of(value) : "undefined";

    /// <summary><c>JSON.stringify(obj[key])</c>: <c>undefined</c> where there is no such key, as a template literal prints it.</summary>
    public static string Json(JsonObject? obj, string key) => obj is not null && obj.TryGetPropertyValue(key, out var value) ? Js.Stringify(value) : "undefined";
}
