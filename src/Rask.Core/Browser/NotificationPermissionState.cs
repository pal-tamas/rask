namespace Rask.Core.Browser;

/// <summary>The user's decision on a notification-permission prompt (<c>Notification.permission</c>).</summary>
public enum NotificationPermissionState
{
    /// <summary>Not yet decided — the browser will prompt on the next request.</summary>
    Default,

    /// <summary>Granted; notifications (and push) are allowed.</summary>
    Granted,

    /// <summary>Denied; blocked until the user changes the site setting.</summary>
    Denied
}
