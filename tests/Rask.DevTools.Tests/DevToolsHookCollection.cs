namespace Rask.DevTools.Tests;

/// <summary>
///     The test classes that start a Development host with the devtools attached.
/// </summary>
/// <remarks>
///     Such a host installs its probe into <c>RaskDevToolsHook.Probe</c>, which is process-wide. Run in parallel, one
///     class's host would replace another's probe in the middle of an assertion about it, so they share one collection
///     that never runs alongside itself.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DevToolsHookCollection
{
    /// <summary>The collection's name, as the member classes' <c>[Collection]</c> attributes spell it.</summary>
    public const string Name = "RaskDevToolsHook (process-wide)";
}
