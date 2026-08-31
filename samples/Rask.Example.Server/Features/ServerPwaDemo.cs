using System.Text;
using System.Text.Json;
using Rask.Core.Browser;
using Rask.Example.Shared;

namespace Rask.Example.Server.Features;

/// <summary>
///     A live PWA demo running on the <b>Server</b> host: local notifications
///     (<see cref="INotifications" />), Web Push (<see cref="IWebPush" /> + this app's
///     <c>Rask.WebPush</c> backend), and the installed-app badge (<see cref="IBadge" />). These APIs are
///     transport-agnostic, so the same code runs here over the WebSocket as it does on WASM.
///     <see cref="ServerPwaPage" /> hosts this demo (with its source) in the showcase.
/// </summary>
public sealed partial class ServerPwaDemo(INotifications notifications, IWebPush push, IBadge badge, HttpClient http) : Component
{
    // Fallback VAPID public key matching the demo backend (PushBackend.DemoPublicKey). The live key comes
    // from GET /_push/key, so the client and server never drift.
    private const string DemoVapidPublicKey =
        "BIl5ANiAgh51-r7wwTyN047Hn3FWTCgLl9cGff1qa5vrft1DmS3jSa-JhTf3PfC6qa_G33YNeNVKT-yyP_6Jqik";

    private string? _notifyStatus;
    private string? _pushStatus;
    private bool _subscribed;
    private string? _badgeStatus;
    private int _badgeCount;

    protected override Component? Render() =>
    [
        Div.Class($"{Ui.Card} shadow-sm border-0 mb-3")[
            Div.Class(Ui.CardBody)[
                H6.Class("font-bold")[Icon.Name(IconName.Bell).Class("me-2"), "Local notification (INotifications)"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "Requests permission, then shows a notification straight from C# — driven over the live ",
                    "WebSocket. Trigger it from this button so the prompt rides a user gesture."
                ],
                Button.Class($"{Ui.BtnPrimary} mb-2").Id("pwa-notify").OnClickAsync(ShowNotification)[
                    "Show a notification"],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("pwa-notify-status")[_notifyStatus ?? "(idle)"]]
            ]
        ],

        Div.Class($"{Ui.Card} shadow-sm border-0 mb-3")[
            Div.Class(Ui.CardBody)[
                H6.Class("font-bold")[Icon.Name(IconName.Broadcast).Class("me-2"), "Web Push (IWebPush)"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "Subscribes with this app's VAPID key and registers with its ", Code["Rask.WebPush"],
                    " backend, then sends a real push that the service worker shows even when the tab is ",
                    "closed — the full loop in one Server app. Install the app for the best experience."
                ],
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Button.Class(Ui.BtnOutlinePrimary).Id("pwa-push").OnClickAsync(EnablePush)[
                        "Enable push (subscribe)"],
                    Button
                        .Class(Ui.BtnPrimary)
                        .Id("pwa-push-send")
                        .Disabled(!_subscribed)
                        .OnClickAsync(SendTestPush)[
                        "Send a test push"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("pwa-push-status")[_pushStatus ?? "(idle)"]]
            ]
        ],

        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                H6.Class("font-bold")[Icon.Name(IconName.AppIndicator).Class("me-2"), "App badge (IBadge)"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "Sets a count on the installed app's icon — install the PWA first, then watch the icon. ",
                    "A silent no-op in a normal browser tab."
                ],
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Button.Class(Ui.BtnOutlinePrimary).Id("pwa-badge-inc").OnClickAsync(BumpBadge)[
                        "Increment badge"],
                    Button.Class(Ui.BtnOutlineDanger).Id("pwa-badge-clear").OnClickAsync(ClearBadge)[
                        "Clear badge"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("pwa-badge-status")[_badgeStatus ?? "(idle)"]]
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
                Body = "A local notification, shown from a Rask.Server app.",
                Tag = "rask-server-pwa-demo"
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

            var vapidKey = await TryGetServerVapidKey() ?? DemoVapidPublicKey;
            var sub = await push.GetSubscriptionAsync() ?? await push.SubscribeAsync(vapidKey);
            _subscribed = true;

            // Register the subscription with this app's backend ({ endpoint, p256dh, auth }); the secrets
            // are never rendered to the page.
            var json = $"{{\"endpoint\":\"{sub.Endpoint}\",\"p256dh\":\"{sub.P256dh}\",\"auth\":\"{sub.Auth}\"}}";
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await http.PostAsync("_push/subscribe", content);
            _pushStatus = response.IsSuccessStatusCode
                ? "Subscribed and registered with the backend — click \"Send a test push\"."
                : $"Subscribed, but the backend returned {(int)response.StatusCode}.";
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
            // The deep-link URL uses the type-safe generated route so a renamed page is a compile error.
            var body = $"{{\"title\":\"Rask push\",\"body\":\"Delivered by Rask.WebPush from the server.\",\"url\":\"{Routes.ServerPwaPage()}\"}}";
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var response = await http.PostAsync("_push/send", content);
            _pushStatus = response.IsSuccessStatusCode
                ? "Push sent — watch for the notification."
                : $"Backend returned {(int)response.StatusCode}.";
        }
        catch (Exception ex)
        {
            _pushStatus = "Failed: " + ex.Message;
        }
    }

    // The backend's current VAPID public key, or null when it can't be reached.
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
