namespace Rask;

/// <summary>
/// How a browser app differs from a default one.
/// </summary>
/// <remarks>
/// <para>
/// Every battery the <c>Rask</c> package brings to the browser is on, so an app that configures nothing
/// is a complete app. What is written here is the exceptions.
/// </para>
/// <para>
/// <b>The browser set is smaller than the server's, and deliberately so.</b> The data batteries —
/// <c>Rask.Data</c>, <c>Rask.SQLite.Browser</c>, <c>Rask.Jobs</c> — all need EF Core, and EF Core does not
/// survive the trimmer in a browser build. Shipping them by default would force <c>PublishTrimmed=false</c>
/// on every WebAssembly app and charge every visitor the difference on first load, including apps with no
/// database at all. A local-first app references those three itself, and takes the untrimmed build knowingly.
/// </para>
/// </remarks>
public sealed class RaskWasmOptions
{
    /// <summary>The source-generated CQRS mediator. Reflection-free, so it trims cleanly.</summary>
    public Battery Cqrs { get; } = new();

    /// <summary>
    /// Caches, dedups and invalidates dispatched queries for the session. Turning the mediator off takes
    /// this with it — there is nothing left to cache.
    /// </summary>
    public Battery Query { get; } = new();

    // Remote dispatch (Rask.Cqrs.Client) deliberately has NO switch here. It is referenced by this
    // package, but wiring it needs an endpoint to dispatch to, which only the app knows — and registering
    // it without one would replace the local dispatcher with one that cannot reach anything. So it stays
    // an explicit AddRaskCqrsClient(...) call. A switch that did nothing would be worse than no switch:
    // an accepted-and-disregarded option is this repo's most expensive bug class.
}
