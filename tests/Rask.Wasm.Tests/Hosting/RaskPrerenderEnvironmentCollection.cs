namespace Rask.Wasm.Tests.Hosting;

/// <summary>
///     Serialises the classes that read or write <c>RASK_PRERENDER_OUT</c> and the process-wide
///     <c>RaskWasmBatteryRegistry</c>.
/// </summary>
/// <remarks>
///     Both are process-global. One class sets the variable to drive a real prerender pass; another
///     asserts it is unset, because prerendering must be asked for rather than inferred. Run in
///     parallel those two are a coin toss — the same race that made the diagnostics-sink tests flaky.
/// </remarks>
[CollectionDefinition("RaskPrerenderEnvironment", DisableParallelization = true)]
public class RaskPrerenderEnvironmentCollection;
