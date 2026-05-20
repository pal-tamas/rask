namespace Rask.Server.JSInterop;

/// <summary>
///     Scoped service that exposes the per-session <see cref="LiveSession" /> to other
///     scoped services in the same DI scope — notably <see cref="RaskJSRuntime" />, which
///     needs to enqueue interop calls onto the session's pending list and trigger a render.
///     <see cref="LiveSessionStore" /> sets <see cref="Session" /> once per session right
///     after construction; the accessor stays null in any DI scope not tied to a session
///     (e.g. unit-test container, app-level singletons), so <see cref="RaskJSRuntime" /> can
///     throw a clear "no current session" error in that case.
/// </summary>
internal sealed class LiveSessionAccessor
{
    public LiveSession? Session { get; set; }
}
