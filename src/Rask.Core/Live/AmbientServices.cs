using Rask.Core.Forms;

namespace Rask.Core.Live;

/// <summary>
///     The service provider of whatever is running now on behalf of a session: the event handler being
///     dispatched, else the render in progress.
/// </summary>
/// <remarks>
///     For code that is called from a component but takes no provider — a static facade, a reader — and
///     must still reach the session's scoped services rather than a process-wide instance, which in a
///     multi-user host would hand one visitor another's state. The handler scope is asked first because
///     it is the only one set while a handler runs; <see cref="LiveRenderContext.Current" /> covers
///     renders, including the first response and a WASM app's single scope. Null outside both — a
///     hosted service, a timer started before any session — and a caller should say so rather than guess.
/// </remarks>
internal static class AmbientServices
{
    public static IServiceProvider? Current => DispatchServicesScope.Current ?? LiveRenderContext.Current?.Services;
}
