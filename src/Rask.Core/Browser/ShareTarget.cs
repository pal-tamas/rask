using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     Registers the installed app as a share target (<c>share_target</c>,
///     <see href="https://developer.mozilla.org/en-US/docs/Web/Manifest/share_target" />) so the OS share
///     sheet can hand content to it.
/// </summary>
/// <param name="Action">URL the shared data is delivered to (resolved against the page when applied).</param>
/// <param name="Params">Maps the shared title/text/url onto query/form fields.</param>
/// <param name="Method">HTTP method, <c>"GET"</c> (default) or <c>"POST"</c>.</param>
/// <param name="Enctype">Encoding for POST, e.g. <c>"multipart/form-data"</c>.</param>
public sealed record ShareTarget(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("params")] ShareTargetParams Params,
    [property: JsonPropertyName("method"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Method = null,
    [property: JsonPropertyName("enctype"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Enctype = null);
