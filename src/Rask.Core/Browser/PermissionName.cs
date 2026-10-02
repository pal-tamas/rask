namespace Rask.Core.Browser;

/// <summary>A queryable browser permission (the names the Permissions API accepts).</summary>
public enum PermissionName
{
    /// <summary><c>geolocation</c> — see <c>Navigator.Geolocation</c>.</summary>
    Geolocation,

    /// <summary><c>notifications</c>.</summary>
    Notifications,

    /// <summary><c>camera</c>.</summary>
    Camera,

    /// <summary><c>microphone</c>.</summary>
    Microphone,

    /// <summary><c>clipboard-read</c> — see <c>Navigator.Clipboard</c>.</summary>
    ClipboardRead,

    /// <summary><c>clipboard-write</c> — see <c>Navigator.Clipboard</c>.</summary>
    ClipboardWrite,

    /// <summary><c>persistent-storage</c>.</summary>
    PersistentStorage
}
