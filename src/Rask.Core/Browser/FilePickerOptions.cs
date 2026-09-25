using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>Tuning for an open/save file picker (the <c>types</c> filter of the File System Access API).</summary>
public sealed record FilePickerOptions
{
    /// <summary>Human label for the accepted file group (the <c>description</c> of the type filter).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>
    ///     Accepted types as MIME → extensions, e.g. <c>{ ["text/plain"] = [".txt", ".md"] }</c> (extensions
    ///     include the leading dot). Omitted lets the user pick any file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Accept { get; init; }
}
