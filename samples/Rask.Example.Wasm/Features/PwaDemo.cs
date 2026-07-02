using System.Text;
using System.Text.Json;
using Rask.Core.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     A live, WASM-only PWA demo: local notifications (<see cref="INotifications" />), Web Push
///     readiness (<see cref="IWebPush" />), and the installed-app badge (<see cref="IBadge" />). These
///     APIs are WASM-only; <see cref="PwaPage" /> hosts this demo (with its source) in the showcase.
/// </summary>
public sealed class PwaDemo(INotifications notifications, IWebPush push, IBadge badge, HttpClient http) : Component
{
    // Fallback VAPID public key for the standalone static showcase (no backend to ask). When a backend
    // is present the key comes from GET /_push/key instead, so the two never drift.
    private const string DemoVapidPublicKey =
        "BIl5ANiAgh51-r7wwTyN047Hn3FWTCgLl9cGff1qa5vrft1DmS3jSa-JhTf3PfC6qa_G33YNeNVKT-yyP_6Jqik";

    private string? _notifyStatus;
    private string? _pushStatus;
    private bool _subscribed;
    private string? _badgeStatus;
    private int _badgeCount;

    protected override Component? Render() =>
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
                    "Subscribes with a demo VAPID key and registers with this app's ", Code()["Rask.WebPush"],
                    " backend, then sends a real push that the service worker shows even when the tab is ",
                    "closed. Run the hosted sample (", Code()["Rask.Example.Wasm.Host"],
                    ") for the full loop — see ", Code()["docs/pwa.md"], "."
                ],
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "pwa-push", OnClickAsync: EnablePush)[
                        "Enable push (subscribe)"],
                    Button(Class: "btn btn-primary btn-sm", Id: "pwa-push-send", Disabled: !_subscribed, OnClickAsync: SendTestPush)[
                        "Send a test push"]
                ],
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

            // Use the backend's VAPID public key when there is one, so the client and server can't
            // drift; fall back to the baked-in demo key on the static showcase.
            var vapidKey = await TryGetServerVapidKey() ?? DemoVapidPublicKey;
            var sub = await push.GetSubscriptionAsync() ?? await push.SubscribeAsync(vapidKey);
            _subscribed = true;

            // Register the subscription with this app's backend. The flat { endpoint, p256dh, auth }
            // shape is what the /_push/subscribe endpoint expects; the secrets are never rendered to
            // the page. On the standalone static showcase there is no backend, so a failure is expected.
            try
            {
                var json = $"{{\"endpoint\":\"{sub.Endpoint}\",\"p256dh\":\"{sub.P256dh}\",\"auth\":\"{sub.Auth}\"}}";
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await http.PostAsync("_push/subscribe", content);
                _pushStatus = response.IsSuccessStatusCode
                    ? "Subscribed and registered with the backend — click \"Send a test push\"."
                    : $"Subscribed, but the backend returned {(int)response.StatusCode}.";
            }
            catch (HttpRequestException)
            {
                _pushStatus = "Subscribed. No backend here (static showcase) — run Rask.Example.Wasm.Host for the full loop.";
            }
        }
        catch (Exception ex)
        {
            _pushStatus = "Failed: " + ex.Message;
        }
    }

    private async Task SendTestPush()
    {
        try
        {
            // Ask the backend to deliver a push to every stored subscription. The browser's service
            // worker shows the notification — even if this tab is closed. The deep-link URL uses the
            // type-safe generated route so a renamed page is a compile error, not a dead link.
            var body = $"{{\"title\":\"Rask push\",\"body\":\"Delivered by Rask.WebPush.\",\"url\":\"{Routes.PwaPage()}\"}}";
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await http.PostAsync("_push/send", content);
            _pushStatus = response.IsSuccessStatusCode
                ? "Push sent — watch for the notification."
                : $"Backend returned {(int)response.StatusCode}.";
        }
        catch (HttpRequestException)
        {
            _pushStatus = "No backend here (static showcase) — run Rask.Example.Wasm.Host for the full loop.";
        }
        catch (Exception ex)
        {
            _pushStatus = "Failed: " + ex.Message;
        }
    }

    // The backend's current VAPID public key, or null when there is no backend (static showcase).
    private async Task<string?> TryGetServerVapidKey()
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync("_push/key"));
            return doc.RootElement.TryGetProperty("publicKey", out var key) ? key.GetString() : null;
        }
        catch (HttpRequestException)
        {
            return null;
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
