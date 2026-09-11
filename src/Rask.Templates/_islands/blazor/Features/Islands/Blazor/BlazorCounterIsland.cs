namespace Company.RaskServer.Features.Islands;

/// <summary>
///     A real Blazor component hosted as an ordinary Rask component.
/// </summary>
/// <remarks>
///     <para>
///         The type argument is the Razor component; the Razor SDK compiles <c>BlazorCounter.razor</c>
///         untouched. It is rendered server-side into the FIRST response, and its own <c>@onclick</c>
///         works with no circuit — Rask rewrites Blazor's handler ids over the socket it already has.
///     </para>
///     <para>
///         Deliberately NOT opaque while static: an opaque subtree makes the differ skip its children,
///         and the island would freeze after the first paint. The type argument is DAM-annotated by
///         <c>BlazorComponent&lt;T&gt;</c> itself, which is what keeps the hosted component's
///         <c>[Parameter]</c> setters alive under a trimmed publish — without it the island renders
///         EMPTY with a green build.
///     </para>
/// </remarks>
public sealed partial class BlazorCounterIsland : Rask.Blazor.BlazorComponent<BlazorCounter>
{
}
