using System.Diagnostics.CodeAnalysis;

namespace Rask.Core.Diagnostics.DevTools;

/// <summary>
///     Whether this build carries Rask DevTools at all — the build-time half of the devtools gate.
/// </summary>
/// <remarks>
///     <para>
///         Set by <c>Rask.DevTools.targets</c> as a <c>RuntimeHostConfigurationOption</c> with
///         <c>Trim="true"</c>: <c>true</c> in a Debug build, <c>false</c> otherwise. The trimmer substitutes
///         the literal value, so a Release publish folds every <c>if (IsEnabled …)</c> branch in the
///         framework to nothing — the probe call sites, the loader and whatever they would have kept alive.
///         An untrimmed build reads it once at type initialisation and the JIT folds the static readonly.
///     </para>
///     <para>
///         Absent — an app that never referenced the package — reads as <c>false</c>.
///     </para>
///     <para>
///         This is not the runtime half. A Debug build carries the devtools; whether they switch on is still
///         decided per host by the environment (Development only), so a Debug build that somebody runs in
///         Production shows nothing.
///     </para>
/// </remarks>
internal static class RaskDevToolsFeature
{
    /// <summary>The <c>AppContext</c> switch name, shared with <c>Rask.DevTools.targets</c>.</summary>
    internal const string SwitchName = "Rask.DevTools.IsEnabled";

    /// <summary>True when the build carries Rask DevTools.</summary>
    [FeatureSwitchDefinition(SwitchName)]
    internal static bool IsEnabled { get; } = AppContext.TryGetSwitch(SwitchName, out var enabled) && enabled;
}
