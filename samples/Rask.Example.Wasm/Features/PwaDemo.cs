using Rask.Core.Routing;
using Rask.Example.Shared;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     A live, WASM-only PWA demo: local notifications (<see cref="INotifications" />) and Web Push
///     readiness (<see cref="IWebPush" />). Lives in the WASM host (not the shared showcase) because
///     these APIs are WASM-only; it's surfaced in the sidebar via a host-registered
///     <see cref="ShowcaseNavEntry" /> (see Program.cs) and nests in the shared
///     <see cref="ShowcaseLayout" /> like every other page.
/// </summary>
[Route("pwa")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class PwaDemo(INotifications notifications, IWebPush push) : Component
{
    private string? _notifyStatus;
    private string? _pushStatus;

    protected override RenderResult Head => Title()["PWA — Rask"];

    protected override RenderResult Render() =>
    [
        H1(Class: "h2 mb-1")["PWA — notifications & push"],
        P(Class: "text-secondary")[
            "A live demo of the WASM-only PWA APIs. This site is itself an installable, offline PWA — ",
            "install it from your browser's address bar, then try the buttons below."
        ],

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

        Div(Class: "card shadow-sm border-0")[
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
}
