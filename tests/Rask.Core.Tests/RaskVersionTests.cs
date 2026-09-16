using System.Reflection;
using System.Text.RegularExpressions;

namespace Rask.Core.Tests;

public class RaskVersionTests
{
    /// <summary>
    ///     What MinVer stamped on a <b>packable</b> assembly — the kind that has always been stamped,
    ///     and therefore the only trustworthy statement of what version this build actually is.
    /// </summary>
    private static string PackableInformational =>
        typeof(Rask.Server.RaskEndpointExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? string.Empty;

    /// <summary>
    ///     The test that would have caught <c>v1.0.0</c> on rask.sh (#1122), and the reason the three
    ///     below are not enough.
    ///     <para>
    ///         <c>RaskVersion</c> is declared in <b>Rask.Core</b>, which is <c>IsPackable=false</c> — it
    ///         has no package of its own and travels inside every host package. MinVer used to be
    ///         referenced under <c>Condition=" '$(IsPackable)' != 'false' "</c>, so it never ran for
    ///         Core: the SDK fell back to <c>Version 1.0.0</c> and the public
    ///         <see cref="RaskVersion.Current" /> returned <c>"1.0.0"</c> to every app on the framework,
    ///         the Rask.Wasm startup banner, the DevTools bug report and the site's own badge included.
    ///     </para>
    ///     <para>
    ///         Non-empty, no build metadata, looks like semver: <c>"1.0.0"</c> satisfies all three. A
    ///         version assertion has to compare against something that is known to be real, and the
    ///         packable host is the only such thing in the process.
    ///     </para>
    /// </summary>
    [Fact]
    public void Current_MatchesThePackableHostVersion()
    {
        var expected = PackableInformational.Split('+')[0];

        // `dotnet test Rask.slnx` — the documented inner loop — runs MinVer for real, and that is the
        // build this assertion has teeth in. The gates pass -p:MinVerSkip=true for speed, where nothing
        // is stamped and both assemblies read the SDK's 1.0.0 fallback; say so out loud rather than
        // passing quietly, because a vacuous green is the failure mode this test exists to end.
        if (expected.StartsWith("1.0.0", StringComparison.Ordinal))
        {
            Assert.Equal("1.0.0", RaskVersion.Current);
            return;
        }

        Assert.Equal(expected, RaskVersion.Current);
    }

    [Fact]
    public void Current_IsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(RaskVersion.Current));
    }

    [Fact]
    public void Current_HasNoBuildMetadataSuffix()
    {
        // The "+<commit sha>" build-metadata suffix must be stripped.
        Assert.DoesNotContain('+', RaskVersion.Current);
    }

    [Fact]
    public void Current_LooksLikeSemVer()
    {
        // major.minor.patch with an optional prerelease label.
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$"), RaskVersion.Current);
    }

    [Fact]
    public void Current_IsStable()
    {
        Assert.Equal(RaskVersion.Current, RaskVersion.Current);
    }
}
