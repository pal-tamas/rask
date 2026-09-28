namespace Rask.Core.Components;

/// <summary>
///     Show the PWA install prompt from a click gesture (works on Server, unlike the imperative
///     <c>IInstallPrompt</c>). <see cref="OnOutcome" /> receives <c>"accepted"</c>, <c>"dismissed"</c>, or
///     <c>"unavailable"</c> — the last when the app isn't installable (it needs a web manifest + service worker
///     over HTTPS; on Server that means <c>AddRaskPwa</c>).
/// </summary>
[RaskChainGroup(typeof(global::Rask.Trigger))]
public sealed class InstallTrigger : Component
{
    /// <summary>Invoked with the install outcome: <c>"accepted"</c>, <c>"dismissed"</c>, or <c>"unavailable"</c>.</summary>
    public Callback<string?> OnOutcome { get; set; }

    /// <summary>Renders your trigger element; its click shows the browser's install prompt.</summary>
    public required Func<IReadOnlyDictionary<string, string?>, Component> Template { get; set; }

    /// <inheritdoc />
    protected override Component Render() => Template!(GestureBridge.Attr("install.prompt", OnOutcome));
}
