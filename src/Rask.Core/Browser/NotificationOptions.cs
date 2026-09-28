using System.Text.Json.Serialization;

namespace Rask.Core.Browser;

/// <summary>
///     Options for a local notification
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/Notification/Notification" />). Unset
///     members take the browser default.
/// </summary>
public sealed record NotificationOptions
{
    /// <summary>Body text shown below the title.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Body { get; init; }

    /// <summary>Icon URL.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; init; }

    /// <summary>Badge URL (monochrome, for constrained UIs).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Badge { get; init; }

    /// <summary>Tag — a new notification with the same tag replaces the previous one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tag { get; init; }

    /// <summary>Keep the notification visible until the user interacts with it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequireInteraction { get; init; }

    /// <summary>Suppress sound/vibration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Silent { get; init; }
}
