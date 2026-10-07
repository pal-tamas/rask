using Rask.Core.Routing;

namespace Rask.Core.Messaging;

/// <summary>The one control a toast carries beside its message: a button that runs something, or a real link.</summary>
/// <param name="Label">Its text — <c>"Undo"</c>.</param>
/// <param name="Run">What pressing it does; the toast is dismissed after. Unset for a link.</param>
public sealed record ToastAction(string Label, Callback Run)
{
    /// <summary>Where it goes, when the action is a link rather than a button: <c>.Action("View", url)</c>.</summary>
    public RouteUrl? Href { get; init; }
}
