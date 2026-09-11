using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary>
///     <see cref="INotifications" /> + <see cref="IBadge" /> — raise a local notification and set the app-icon
///     badge from the page. Both work on every host, through the browser's Notifications and Badging APIs
///     (a badge only shows on an installed PWA).
/// </summary>
public sealed partial class NotificationsDemo(INotifications notifications, IBadge badge) : Component
{
    private string? _status;

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline)
                        .Id("notif-permission")
                        .OnClick(RequestPermission)["Request permission"],
                    UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline)
                        .Id("notif-show")
                        .OnClick(Notify)["Notify"],
                    UiButton.Variant(UiVariant.Outline)
                        .Id("badge-set")
                        .OnClick(SetBadge)["Set badge 3"],
                    UiButton.Tone(UiTone.Error).Variant(UiVariant.Outline)
                        .Id("badge-clear")
                        .OnClick(ClearBadge)["Clear badge"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("notif-status")[_status ?? "(idle)"]]
            ];

    private async Task RequestPermission()
    {
        if (!await notifications.IsSupportedAsync())
        {
            _status = "Notifications not supported on this host";
            return;
        }

        _status = $"Permission: {await notifications.RequestPermissionAsync()}";
    }

    private async Task Notify()
    {
        if (!await notifications.IsSupportedAsync())
        {
            _status = "Notifications not supported on this host";
            return;
        }

        // Showing without permission throws (matching the browser), so gate on it and prompt the user first.
        if (await notifications.PermissionAsync() != NotificationPermission.Granted)
        {
            _status = "Grant permission first";
            return;
        }

        await notifications.ShowAsync("Rask", new NotificationOptions { Body = "Hello from your Rask app.", Tag = "demo" });
        _status = "Notification sent";
    }

    private async Task SetBadge()
    {
        if (!await badge.IsSupportedAsync())
        {
            _status = "Badge not supported on this host";
            return;
        }

        await badge.SetAsync(3);
        _status = "Badge set to 3";
    }

    private async Task ClearBadge()
    {
        await badge.ClearAsync();
        _status = "Badge cleared";
    }
}
