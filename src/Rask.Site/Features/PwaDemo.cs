using System.Buffers.Text;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     A live, WASM-only PWA demo: local notifications (MDN's <c>Notification</c>), a Web Push subscription
///     (MDN's <c>PushManager</c>), and the installed-app badge (MDN's <c>navigator.setAppBadge</c>), all from
///     Rask.Web. <see cref="PwaPage" /> hosts this demo (with its source) in the showcase.
/// </summary>
public sealed partial class PwaDemo(HttpClient http) : Component
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
        Ui.Card.Class("mb-3")[
                H6.Class("font-bold")[Ui.Icon.Name(Ui.IconName.BellAlert).Class("size-5 me-2"), "Local notification (Notification)"],
                P.Class("text-sm text-ui-muted")[
                    "Requests permission, then shows a notification straight from C# — no server."
                ],
                Ui.Button.Primary.Class("mb-2").Id("pwa-notify").OnClick(ShowNotification)["Show a notification"],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("pwa-notify-status")[_notifyStatus ?? "(idle)"]]
            ],

        Ui.Card.Class("mb-3")[
                H6.Class("font-bold")[Ui.Icon.Name(Ui.IconName.Signal).Class("size-5 me-2"), "Web Push (PushManager)"],
                P.Class("text-sm text-ui-muted")[
                    "Subscribes, then hands the subscription to a ", Code["RaskApp"],
                    " host's Web Push battery at ", Code["/_rask/push/subscribe"],
                    ". The host sends with ", Code["Push.Send(…)"],
                    ", and the service worker shows it even when the tab is closed. This showcase has no host, ",
                    "so it stops at the subscription — see ", Code["docs/webpush.md"], "."
                ],
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Id("pwa-push").OnClick(EnablePush)["Enable push (subscribe)"],
                    Ui.Button.Primary
                        .Id("pwa-push-send")
                        .Disabled(!_subscribed)
                        .OnClick(SendTestPush)["Send a test push"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("pwa-push-status")[_pushStatus ?? "(idle)"]]
            ],

        Ui.Card[
                H6.Class("font-bold")[Ui.Icon.Name(Ui.IconName.Squares2x2).Class("size-5 me-2"), "App badge (Navigator.SetAppBadge)"],
                P.Class("text-sm text-ui-muted")[
                    "Sets a count on the installed app's icon — install the PWA first, then watch the icon. ",
                    "A silent no-op in a normal browser tab."
                ],
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Id("pwa-badge-inc").OnClick(BumpBadge)["Increment badge"],
                    Ui.Button.Red.Id("pwa-badge-clear").OnClick(ClearBadge)["Clear badge"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("pwa-badge-status")[_badgeStatus ?? "(idle)"]]
            ]
    ];

    private async Task ShowNotification()
    {
        try
        {
            if (!await Notification.IsSupported)
            {
                _notifyStatus = "Notifications not supported in this browser";
                return;
            }

            var permission = await Notification.RequestPermission();
            if (permission != Types.NotificationPermission.Granted)
            {
                _notifyStatus = $"Permission: {permission}";
                return;
            }

            await using var shown = await Notification.Create("Hello from Rask", new()
            {
                Body = "A local notification, shown from C#.",
                Tag = "rask-pwa-demo"
            });
            _notifyStatus = "Notification shown";
        }
        catch (JSException ex)
        {
            _notifyStatus = "Failed: " + ex.Message;
        }
    }

    private async Task EnablePush()
    {
        try
        {
            if (!await PushManager.IsSupported)
            {
                _pushStatus = "Push not supported in this browser";
                return;
            }

            var permission = await Notification.RequestPermission();
            if (permission != Types.NotificationPermission.Granted)
            {
                _pushStatus = $"Permission: {permission}";
                return;
            }

            // The page shell registered rask-sw.js, which shows each push; Ready settles once it is active.
            await using var worker = await Navigator.ServiceWorker.Ready;

            // Use the backend's VAPID public key when there is one, so the client and server can't
            // drift; fall back to the baked-in demo key on the static showcase. Subscribing again with
            // the same key answers the subscription the browser already has.
            var vapidKey = await TryGetServerVapidKey() ?? DemoVapidPublicKey;
            await using var subscription = await worker.PushManager.Subscribe(new()
            {
                UserVisibleOnly = true,
                ApplicationServerKey = Base64Url.DecodeFromChars(vapidKey)
            });
            _subscribed = true;

            // Hand the subscription to the host's Web Push battery in MDN's own toJSON() shape; the
            // secrets are never rendered to the page. On the standalone static showcase there is no
            // backend, so a failure is expected.
            try
            {
                using var response = await http.PostAsJsonAsync("_rask/push/subscribe", await subscription.ToJSON(),
                    PushJsonContext.Default.PushSubscriptionJSON);
                _pushStatus = response.IsSuccessStatusCode
                    ? "Subscribed and registered with the backend — click \"Send a test push\"."
                    : $"Subscribed, but the backend returned {(int)response.StatusCode}.";
            }
            catch (HttpRequestException)
            {
                _pushStatus = "Subscribed. No backend here (static showcase), so nothing will send to it.";
            }
        }
        catch (JSException ex)
        {
            _pushStatus = "Failed: " + ex.Message;
        }
    }

    private async Task SendTestPush()
    {
        try
        {
            // Sending is server code, so a host maps one endpoint of its own for this button: a POST to
            // /push/test whose handler awaits Push.Send with a WebPushMessage.Text. The browser's service worker shows the notification — even if this tab is closed.
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

    // setAppBadge is a method, so there is nothing to ask first: a browser without it rejects the call.
    private async Task BumpBadge()
    {
        try
        {
            await Navigator.SetAppBadge(++_badgeCount);
            _badgeStatus = $"Badge set to {_badgeCount} (visible on the installed icon)";
        }
        catch (JSException ex)
        {
            _badgeStatus = "App badges not supported in this browser: " + ex.Message;
        }
    }

    private async Task ClearBadge()
    {
        try
        {
            _badgeCount = 0;
            await Navigator.ClearAppBadge();
            _badgeStatus = "Badge cleared";
        }
        catch (JSException ex)
        {
            _badgeStatus = "Failed: " + ex.Message;
        }
    }
}
