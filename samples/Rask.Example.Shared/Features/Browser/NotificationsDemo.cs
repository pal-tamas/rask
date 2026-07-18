using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="INotifications" /> + <see cref="IBadge" /> — raise a local notification and set the app-icon
///     badge from the page. Both work on every host; in the native shell they resolve to real OS backends
///     (UNUserNotificationCenter / NotificationManager and the native app-icon badge), which a WebView can't do.
/// </summary>
public sealed class NotificationsDemo(INotifications notifications, IBadge badge) : Component
{
    private string? _status;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                BsStack(Gap: 2, WrapItems: true, Class: Margin.Bottom(2))[
                    BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Id: "notif-permission",
                        OnClickAsync: RequestPermission)["Request permission"],
                    BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Id: "notif-show",
                        OnClickAsync: Notify)["Notify"],
                    BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Id: "badge-set",
                        OnClickAsync: SetBadge)["Set badge 3"],
                    BsButton(Color: BsColor.Danger, Outline: true, Size: BsSize.Sm, Id: "badge-clear",
                        OnClickAsync: ClearBadge)["Clear badge"]
                ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "notif-status")[_status ?? "(idle)"]]
            ]
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
