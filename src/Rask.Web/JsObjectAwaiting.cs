using System.Runtime.CompilerServices;

namespace Rask.Web;

/// <summary>What lets a web object be awaited: <c>await Window.MatchMedia(q)</c> keeps the object it names.</summary>
public static class JsObjectAwaiting
{
    /// <summary>Keeps the object in the browser, and returns it as a handle to dispose of when done.</summary>
    public static TaskAwaiter<T> GetAwaiter<T>(this T value)
        where T : JsObject =>
        value.Keep<T>().AsTask().GetAwaiter();
}
