using System.Reflection;
using System.Text.RegularExpressions;

namespace Rask.Core.Tests.HotReload;

/// <summary>
///     Guards the one build property that decides whether hot reload can apply anything at all in this
///     repo (#637).
///     <para>
///         MinVer sets <c>AssemblyVersion</c> from a <b>target</b>. Hot reload never runs targets —
///         Roslyn's EnC service compiles in-process from the project's <b>evaluated</b> properties, where
///         MinVer has not run and the SDK falls back to <c>Version 1.0.0</c>. Without an evaluation-time
///         pin the two disagree, and every edit comes back as <c>error CS7038: … Changing the version of
///         an assembly reference is not allowed during debugging</c>. Silent in the way that matters: the
///         build is green, the tests pass, the packages are right, and only someone sitting in front of
///         <c>rask dev</c> ever finds out.
///     </para>
///     <para>
///         <b>It only ever bit the packable projects</b>, which is why it hid so long. MinVer is
///         referenced under <c>Condition=" '$(IsPackable)' != 'false' "</c>, so an unpackable project like
///         <c>Rask.Core</c> reads <c>1.0.0.0</c> from disk <i>and</i> under EnC and never mismatches — the
///         error named <c>Rask.Bootstrap</c>. Assert on a packable assembly or this proves nothing.
///     </para>
/// </summary>
public class AssemblyVersionStabilityTests
{
    /// <summary>A <b>packable</b> Rask assembly — the only kind MinVer stamps, so the only kind at risk.</summary>
    private static readonly Assembly Packable = typeof(Rask.Server.RaskEndpointExtensions).Assembly;

    private static string Informational =>
        Packable.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty;

    /// <summary>
    ///     Whether MinVer actually ran for this build. <c>scripts/run-unit-local.sh</c> passes
    ///     <c>-p:MinVerSkip=true</c> for speed, and then there is no stamped version to compare anything
    ///     against — but the pin below must hold either way, which is the point of pinning it.
    /// </summary>
    private static bool MinVerRan => !Informational.StartsWith("1.0.0", StringComparison.Ordinal);

    [Fact]
    public void The_assembly_version_of_a_packable_project_is_pinned_and_not_left_to_a_target()
    {
        // The load-bearing one, and true in every build mode. 1.0.0.0 is the SDK's fallback when Version
        // was never set — exactly what an evaluation-time read produces once the pin is gone. Seeing it
        // here means Directory.Build.props lost the pin and hot reload is broken again.
        Assert.Equal(new Version(0, 0, 0, 0), Packable.GetName().Version);
    }

    [Fact]
    public void The_pinned_version_matches_what_MinVer_would_stamp_for_the_major_being_shipped()
    {
        if (!MinVerRan)
        {
            // Not a silent pass: state what this build is, and re-assert the invariant that still applies.
            Assert.StartsWith("1.0.0", Informational, StringComparison.Ordinal);
            Assert.Equal(new Version(0, 0, 0, 0), Packable.GetName().Version);
            return;
        }

        // The pin is only correct while the major is 0: MinVer stamps $(MinVerMajor).0.0.0, so at v1.0.0 a
        // pinned 0.0.0.0 would understate the binary-compatibility identity. This speaks up on that day
        // rather than letting the pin quietly become wrong.
        var major = int.Parse(Regex.Match(Informational, @"^\d+").Value);

        Assert.True(
            major == 0,
            $"Rask now ships major version {major}, so <AssemblyVersion> in Directory.Build.props must "
            + $"become {major}.0.0.0 (what MinVer's target stamps) — and this test updated with it. "
            + "Leaving it at 0.0.0.0 keeps hot reload working but misstates binary compatibility; "
            + "removing it altogether breaks hot reload again (#637).");
    }

    [Fact]
    public void Pinning_the_assembly_version_does_not_cost_the_real_version()
    {
        // Pinning must not swallow the actual version: it moves to FileVersion and InformationalVersion,
        // which MinVer keeps stamping — what `rask info`, the packages and every bug report read.
        if (!MinVerRan)
        {
            Assert.StartsWith("1.0.0", Informational, StringComparison.Ordinal);
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(Informational));
        Assert.Matches(@"^\d+\.\d+\.\d+", Informational);
    }
}
