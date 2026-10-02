using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>Notification</c> and <c>navigator.setAppBadge</c> from Rask.Web — raise a local notification and
///     set the app-icon badge from the page (a badge only shows on an installed PWA).
/// </summary>
public sealed partial class NotificationsDemo : Component
{
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary.Outline
                        .Id("notif-permission")
                        .OnClick(RequestPermission)["Request permission"],
                    Ui.Button.Primary.Outline
                        .Id("notif-show")
                        .OnClick(Notify)["Notify"],
                    Ui.Button.Outline
                        .Id("badge-set")
                        .OnClick(SetBadge)["Set badge 3"],
                    Ui.Button.Error.Outline
                        .Id("badge-clear")
                        .OnClick(ClearBadge)["Clear badge"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("notif-status")[_status ?? "(idle)"]]
            ];

    private async Task RequestPermission()
    {
        if (!await Notification.IsSupported)
        {
            _status = "Notifications not supported on this host";
            return;
        }

        _status = $"Permission: {await Notification.RequestPermission()}";
    }

    private async Task Notify()
    {
        if (!await Notification.IsSupported)
        {
            _status = "Notifications not supported on this host";
            return;
        }

        // Showing without permission throws (matching the browser), so gate on it and prompt the user first.
        if (await Notification.Permission != Types.NotificationPermission.Granted)
        {
            _status = "Grant permission first";
            return;
        }

        await using var shown = await Notification.Create("Rask", new() { Body = "Hello from your Rask app.", Tag = "demo" });
        _status = "Notification sent";
    }

    // setAppBadge is a method, so there is nothing to ask first: a browser without it rejects the call.
    private async Task SetBadge()
    {
        try
        {
            await Navigator.SetAppBadge(3);
            _status = "Badge set to 3";
        }
        catch (JSException ex)
        {
            _status = "Badge not supported on this host: " + ex.Message;
        }
    }

    private async Task ClearBadge()
    {
        try
        {
            await Navigator.ClearAppBadge();
            _status = "Badge cleared";
        }
        catch (JSException ex)
        {
            _status = "Badge not supported on this host: " + ex.Message;
        }
    }
}
