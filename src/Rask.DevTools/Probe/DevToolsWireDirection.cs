namespace Rask.DevTools.Probe;

/// <summary>Which way a frame crossed the wire, from the inspected page's point of view.</summary>
internal enum DevToolsWireDirection : byte
{
    /// <summary>The page sent it to the app: an event, a navigation, a hello.</summary>
    Out,

    /// <summary>The app sent it to the page: a render frame, an ack, a control frame.</summary>
    In,
}
