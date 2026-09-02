using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="INotifications" /> + <see cref="IBadge" /> — raise a local notification and set the app-icon
///     badge from the page. Both work on every host, through the browser's Notifications and Badging APIs
///     (a badge only shows on an installed PWA).
/// </summary>
public sealed partial class NotificationsDemo(INotifications notifications, IBadge badge) : Component
{
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Button.Type("button").Class(Tw.BtnOutlinePrimary)
                        .Id("notif-permission")
                        .OnClickAsync(RequestPermission)["Request permission"],
                    Button.Type("button").Class(Tw.BtnOutlinePrimary)
                        .Id("notif-show")
                        .OnClickAsync(Notify)["Notify"],
                    Button.Type("button").Class(Tw.BtnOutlineSecondary)
                        .Id("badge-set")
                        .OnClickAsync(SetBadge)["Set badge 3"],
                    Button.Type("button").Class(Tw.BtnOutlineDanger)
                        .Id("badge-clear")
                        .OnClickAsync(ClearBadge)["Clear badge"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("notif-status")[_status ?? "(idle)"]]
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
