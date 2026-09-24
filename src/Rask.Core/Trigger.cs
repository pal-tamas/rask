namespace Rask;

/// <summary>
///     The components that put a browser capability behind a control: <c>Trigger.Fullscreen</c>,
///     <c>Trigger.Install</c>, <c>Trigger.PictureInPicture</c>, <c>Trigger.EyeDropper</c>, <c>Trigger.MediaCapture</c>,
///     <c>Trigger.ScreenOrientation</c>, <c>Trigger.Gesture</c>.
/// </summary>
/// <remarks>
///     Each wraps the element that starts it, because a browser grants these only inside a user gesture. Grouped so
///     typing <c>Trigger.</c> lists them, and in the <c>Rask</c> namespace so <c>using Rask;</c> reaches them.
/// </remarks>
public static partial class Trigger;
