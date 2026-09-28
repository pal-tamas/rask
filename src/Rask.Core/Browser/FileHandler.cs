using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     Associates file types with the installed app (an entry of <c>file_handlers[]</c>,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/Manifest/file_handlers" />) so the OS can
///     launch it to open matching files.
/// </summary>
/// <param name="Action">URL launched to handle the files (resolved against the page when applied).</param>
/// <param name="Accept">MIME type → file extensions map, e.g. <c>{ ["text/csv"] = [".csv"] }</c>.</param>
public sealed record FileHandler(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("accept")] IReadOnlyDictionary<string, string[]> Accept);
