namespace Rask.Core.Live;

/// <summary>Which session an event's scope belongs to: null in the session's own scope, and in a request's.</summary>
internal sealed class EventScopeLink
{
    internal IServiceProvider? Session { get; set; }
}
