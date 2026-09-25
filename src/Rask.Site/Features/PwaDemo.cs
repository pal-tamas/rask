using System.Text;
using System.Text.Json;
using Rask.Core.Browser;
using Rask.Site;

namespace Rask.Site.Features;

/// <summary>
///     A live, WASM-only PWA demo: local notifications (<see cref="INotifications" />), Web Push
///     readiness (<see cref="IWebPush" />), and the installed-app badge (<see cref="IBadge" />). These
///     APIs are WASM-only; <see cref="PwaPage" /> hosts this demo (with its source) in the showcase.
/// </summary>
public sealed partial class PwaDemo(INotifications notifications, IWebPush push, IBadge badge, HttpClient http) : Component
{
    // Fallback VAPID public key for the standalone static showcase (no backend to ask). When a backend
    // is present the key comes from GET /_rask/push/key instead, so the two never drift.
    private const string DemoVapidPublicKey =
        "BIl5ANiAgh51-r7wwTyN047Hn3FWTCgLl9cGff1qa5vrft1DmS3jSa-JhTf3PfC6qa_G33YNeNVKT-yyP_6Jqik";

    private string? _notifyStatus;
    private string? _pushStatus;
    private bool _subscribed;
    private string? _badgeStatus;
    private int _badgeCount;

    protected override Component? Render() =>
    [
        Ui.Card.Class("shadow-sm mb-3")[
                H6.Class("font-bold")[Ui.Icon.Name(Ui.IconName.Bell).Class("me-2"), "Local notification (INotifications)"],
                P.Class("text-sm text-ui-muted")[
                    "Requests permission, then shows a notification straight from C# — no server."
                ],
                Ui.Button.Tone(Ui.Tone.Primary).Class("mb-2").Id("pwa-notify").OnClick(ShowNotification)["Show a notification"],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("pwa-notify-status")[_notifyStatus ?? "(idle)"]]
            ],

        Ui.Card.Class("shadow-sm mb-3")[
                H6.Class("font-bold")[Ui.Icon.Name(Ui.IconName.Signal).Class("me-2"), "Web Push (IWebPush)"],
                P.Class("text-sm text-ui-muted")[
                    "Subscribes, then hands the subscription to a ", Code["RaskApp"],
                    " host's Web Push battery at ", Code["/_rask/push/subscribe"],
                    ". The host sends with ", Code["Push.Send(…)"],
                    ", and the service worker shows it even when the tab is closed. This showcase has no host, ",
                    "so it stops at the subscription — see ", Code["docs/webpush.md"], "."
                ],
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline).Id("pwa-push").OnClick(EnablePush)["Enable push (subscribe)"],
                    Ui.Button.Tone(Ui.Tone.Primary)
                        .Id("pwa-push-send")
                        .Disabled(!_subscribed)
                        .OnClick(SendTestPush)["Send a test push"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("pwa-push-status")[_pushStatus ?? "(idle)"]]
            ],

        Ui.Card.Class("shadow-sm")[
                H6.Class("font-bold")[Ui.Icon.Name(Ui.IconName.Overview).Class("me-2"), "App badge (IBadge)"],
                P.Class("text-sm text-ui-muted")[
                    "Sets a count on the installed app's icon — install the PWA first, then watch the icon. ",
                    "A silent no-op in a normal browser tab."
                ],
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Tone(Ui.Tone.Primary).Variant(Ui.Variant.Outline).Id("pwa-badge-inc").OnClick(BumpBadge)["Increment badge"],
                    Ui.Button.Tone(Ui.Tone.Error).Variant(Ui.Variant.Outline).Id("pwa-badge-clear").OnClick(ClearBadge)["Clear badge"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("pwa-badge-status")[_badgeStatus ?? "(idle)"]]
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

            // Hand the subscription to the host's Web Push battery. The flat { endpoint, p256dh, auth }
            // shape is what /_rask/push/subscribe expects; the secrets are never rendered to the page.
            // On the standalone static showcase there is no backend, so a failure is expected.
            try
            {
                var json = $"{{\"endpoint\":\"{sub.Endpoint}\",\"p256dh\":\"{sub.P256dh}\",\"auth\":\"{sub.Auth}\"}}";
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await http.PostAsync("_rask/push/subscribe", content);
                _pushStatus = response.IsSuccessStatusCode
                    ? "Subscribed and registered with the backend — click \"Send a test push\"."
                    : $"Subscribed, but the backend returned {(int)response.StatusCode}.";
            }
            catch (HttpRequestException)
            {
                _pushStatus = "Subscribed. No backend here (static showcase), so nothing will send to it.";
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
            // Sending is server code, so a host maps one endpoint of its own for this button:
            //   app.MapEndpoints(e => e.MapPost("/push/test", async () =>
            //       await Push.Send(WebPushMessage.Text("Rask push", "Delivered by Rask.WebPush."))));
            // The browser's service worker shows the notification — even if this tab is closed.
            var response = await http.PostAsync("push/test", content: null);
            _pushStatus = response.IsSuccessStatusCode
                ? "Push sent — watch for the notification."
                : $"Backend returned {(int)response.StatusCode}.";
        }
        catch (HttpRequestException)
        {
            _pushStatus = "No backend here (static showcase) — a RaskApp host sends with Push.Send(…).";
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
            using var doc = JsonDocument.Parse(await http.GetStringAsync("_rask/push/key"));
            return doc.RootElement.TryGetProperty("publicKey", out var key) && key.GetString() is { Length: > 0 } value
                ? value
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // No backend, or a static host answering with its HTML fallback.
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
