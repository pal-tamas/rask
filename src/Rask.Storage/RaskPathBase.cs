namespace Rask.Storage;

/// <summary>The deploy's path base, as the Rask host published it, read without referencing <c>Rask.Core</c>.</summary>
/// <remarks>
/// <para>
/// A server or WebAssembly host sets <c>LiveOptions.PathBase</c>, which also publishes the normalized value as
/// <see cref="AppContext" /> data under <see cref="DataName" />. Reading it from there instead of from the Core class is
/// what lets this package start on the SPA and meta lanes, where no copy of Rask.Core exists (#1086).
/// </para>
/// <para>
/// Process-wide, exactly as the value it mirrors is, and read on every use rather than captured, so a host that sets its
/// path base after the storage services are registered is still honoured. Absent — a lane with no Rask host, or a host
/// with no path base — it is empty, which is what an app served at the root wants.
/// </para>
/// </remarks>
internal static class RaskPathBase
{
    /// <summary>The name Rask.Core publishes it under (<c>LiveOptions.PathBaseDataName</c>).</summary>
    internal const string DataName = "Rask.PathBase";

    internal static string Current => AppContext.GetData(DataName) as string ?? "";
}
