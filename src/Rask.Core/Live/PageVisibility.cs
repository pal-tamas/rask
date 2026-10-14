using System.Text.Json;

namespace Rask.Core.Live;

/// <summary>
///     Whether the reader's tab is out of sight: what a polling query waits on, so a page in a background tab does
///     not go on asking the server for rows nobody is looking at. One per session.
/// </summary>
/// <remarks>
///     The browser says so with a <c>visibility</c> frame (<c>rask-visibility.ts</c>), sent when the tab is hidden or
///     shown again and when a connection opens in a hidden tab. Both hosts read it on the route every frame takes.
/// </remarks>
internal sealed class PageVisibility
{
    private readonly Lock _gate = new();

    // Set while the tab is out of sight, and completed the moment it is back.
    private TaskCompletionSource? _shown;

    /// <summary>The tab went out of sight (<paramref name="hidden" />), or came back.</summary>
    public void Set(bool hidden)
    {
        TaskCompletionSource? back = null;
        lock (_gate)
        {
            if (hidden)
            {
                _shown ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else
            {
                back = _shown;
                _shown = null;
            }
        }

        back?.TrySetResult();
    }

    /// <summary>Completes at once while the tab is in sight, and otherwise when it comes back.</summary>
    public Task Shown(CancellationToken cancellationToken)
    {
        TaskCompletionSource? shown;
        lock (_gate)
        {
            shown = _shown;
        }

        return shown is null ? Task.CompletedTask : shown.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Whether <paramref name="frame" /> is the browser's word on the tab, which it then takes.</summary>
    public static bool Heard(JsonElement frame, IServiceProvider services)
    {
        if (!frame.TryGetProperty("type", out var type) || !type.ValueEquals("visibility"u8))
        {
            return false;
        }

        if (services.GetService(typeof(PageVisibility)) is PageVisibility visibility)
        {
            visibility.Set(frame.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True);
        }

        return true;
    }
}
