using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>The query parameter names a share target maps the shared data onto (<c>share_target.params</c>).</summary>
/// <param name="Title">Form field that receives the shared title.</param>
/// <param name="Text">Form field that receives the shared text.</param>
/// <param name="Url">Form field that receives the shared URL.</param>
public sealed record ShareTargetParams(
    [property: JsonPropertyName("title"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Title = null,
    [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonPropertyName("url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null);
