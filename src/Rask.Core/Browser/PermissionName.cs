namespace Rask.Core.Browser;

/// <summary>A queryable browser permission (the names the Permissions API accepts).</summary>
public enum PermissionName
{
    /// <summary><c>geolocation</c> — see <see cref="IGeolocation" />.</summary>
    Geolocation,

    /// <summary><c>notifications</c>.</summary>
    Notifications,

    /// <summary><c>camera</c>.</summary>
    Camera,

    /// <summary><c>microphone</c>.</summary>
    Microphone,

    /// <summary><c>clipboard-read</c> — see <see cref="IClipboard" />.</summary>
    ClipboardRead,

    /// <summary><c>clipboard-write</c> — see <see cref="IClipboard" />.</summary>
    ClipboardWrite,

    /// <summary><c>persistent-storage</c>.</summary>
    PersistentStorage
}
