namespace Rask.Core.Routing;

/// <summary>
/// Marks an assembly whose pages are served only by the app that mounts them, never by the main router.
/// </summary>
/// <remarks>
/// <para>
/// A routed page registers itself when its assembly loads, into a registry every app in the process shares.
/// For an app's own pages that is the point. For a surface an app <em>mounts</em> — the operator console
/// under <c>/_rask</c> — it is a hazard: an app that references the assembly without mounting it (a battery
/// turned off, a host assembled by hand) would still route <c>/_rask/…</c> to pages whose authorization
/// policy was never registered, and answer with a 500 instead of a 404.
/// </para>
/// <para>
/// With this on the assembly, <see cref="RouteRegistry.BuildTree()"/> leaves its pages out and only
/// <see cref="RouteRegistry.BuildTree(System.Reflection.Assembly)"/> — what a mount asks for — includes them.
/// </para>
/// <example><code>[assembly: Rask.Core.Routing.MountOnlyRoutes]</code></example>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class MountOnlyRoutesAttribute : Attribute;
