using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     A live, WASM-only PWA demo: local notifications (<see cref="INotifications" />), Web Push
///     readiness (<see cref="IWebPush" />), and the installed-app badge (<see cref="IBadge" />). These
///     APIs are WASM-only; <see cref="PwaPage" /> hosts this demo (with its source) in the showcase.
/// </summary>
public sealed class PwaDemo(INotifications notifications, IWebPush push, IBadge badge) : Component
{
    private string? _notifyStatus;
    private string? _pushStatus;
    private string? _badgeStatus;
    private int _badgeCount;

    protected override RenderResult Render() =>
    [
        Div(Class: "card shadow-sm border-0 mb-3")[
            Div(Class: "card-body")[
                H6(Class: "fw-bold")[I(Class: "bi bi-bell me-2"), "Local notification (INotifications)"],
                P(Class: "small text-secondary")[
                    "Requests permission, then shows a notification straight from C# — no server."
                ],
                Button(Class: "btn btn-primary btn-sm mb-2", Id: "pwa-notify", OnClickAsync: ShowNotification)[
                    "Show a notification"],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "pwa-notify-status")[_notifyStatus ?? "(idle)"]]
            ]
        ],

        Div(Class: "card shadow-sm border-0 mb-3")[
            Div(Class: "card-body")[
                H6(Class: "fw-bold")[I(Class: "bi bi-broadcast me-2"), "Web Push (IWebPush)"],
                P(Class: "small text-secondary")[
                    "Checks support, requests permission, and registers the service worker. Subscribing needs ",
                    "your own VAPID key + backend — see ", Code()["docs/pwa.md"], "."
                ],
                Button(Class: "btn btn-outline-primary btn-sm mb-2", Id: "pwa-push", OnClickAsync: EnablePush)[
                    "Enable push (register service worker)"],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "pwa-push-status")[_pushStatus ?? "(idle)"]]
            ]
        ],

        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                H6(Class: "fw-bold")[I(Class: "bi bi-app-indicator me-2"), "App badge (IBadge)"],
                P(Class: "small text-secondary")[
                    "Sets a count on the installed app's icon — install the PWA first, then watch the icon. ",
                    "A silent no-op in a normal browser tab."
                ],
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "pwa-badge-inc", OnClickAsync: BumpBadge)[
                        "Increment badge"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "pwa-badge-clear", OnClickAsync: ClearBadge)[
                        "Clear badge"]
                ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "pwa-badge-status")[_badgeStatus ?? "(idle)"]]
            ]
        ]
    ];

    private async Task ShowNotification()
    {
        try
        {
            if (!await notifications.IsSupportedAsync())
            {
                _notifyStatus = "Notifications not supported in this browser";
                return;
            }

            var permission = await notifications.RequestPermissionAsync();
            if (permission != NotificationPermission.Granted)
            {
                _notifyStatus = $"Permission: {permission}";
                return;
            }

            await notifications.ShowAsync("Hello from Rask", new NotificationOptions
            {
                Body = "A local notification, shown from C#.",
                Tag = "rask-pwa-demo"
            });
            _notifyStatus = "Notification shown";
        }
        catch (Exception ex)
        {
            _notifyStatus = "Failed: " + ex.Message;
        }
    }

    private async Task EnablePush()
    {
        try
        {
            if (!await push.IsSupportedAsync())
            {
                _pushStatus = "Push not supported in this browser";
                return;
            }

            var permission = await push.RequestPermissionAsync();
            if (permission != NotificationPermission.Granted)
            {
                _pushStatus = $"Permission: {permission}";
                return;
            }

            await push.RegisterServiceWorkerAsync();
            var existing = await push.GetSubscriptionAsync();
            _pushStatus = existing is null
                ? "Service worker registered — ready to subscribe with your VAPID key."
                : "Already subscribed (endpoint hidden).";
        }
        catch (Exception ex)
        {
            _pushStatus = "Failed: " + ex.Message;
        }
    }

    private async Task BumpBadge()
    {
        try
        {
            if (!await badge.IsSupportedAsync())
            {
                _badgeStatus = "App badges not supported in this browser";
                return;
            }

            await badge.SetAsync(++_badgeCount);
            _badgeStatus = $"Badge set to {_badgeCount} (visible on the installed icon)";
        }
        catch (Exception ex)
        {
            _badgeStatus = "Failed: " + ex.Message;
        }
    }

    private async Task ClearBadge()
    {
        try
        {
            _badgeCount = 0;
            await badge.ClearAsync();
            _badgeStatus = "Badge cleared";
        }
        catch (Exception ex)
        {
            _badgeStatus = "Failed: " + ex.Message;
        }
    }
}
