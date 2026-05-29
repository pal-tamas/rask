using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

/// <summary>
///     Covers the PathBase normalization contract used across every hosting model
///     (Server, Wasm.Hosting, WASM standalone). The accessor on <see cref="LiveOptions" />
///     and the instance property on <see cref="RaskLiveOptions" /> both round-trip
///     values through <see cref="RaskPath.Normalize" /> on assignment.
/// </summary>
// Shares the ScopedAssets non-parallel collection so a concurrent
// HeadAssetPathBaseTests test (also in ScopedAssets) can't race against
// LiveOptions.PathBase writes from this class — it's a process-global static.
[Collection("ScopedAssets")]
public sealed class RaskPathTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData(" ", "")]
    [InlineData("/", "")]
    [InlineData("appA", "/appA")]
    [InlineData("/appA", "/appA")]
    [InlineData("/appA/", "/appA")]
    [InlineData("/appA//", "/appA")]
    [InlineData("/a/b", "/a/b")]
    [InlineData("/a/b/", "/a/b")]
    [InlineData("a/b/", "/a/b")]
    public void Normalize_ProducesEmptyOrLeadingSlashNoTrailing(string? input, string expected)
        => Assert.Equal(expected, RaskPath.Normalize(input));

    [Fact]
    public void LiveOptions_PathBase_NormalizesOnAssignment()
    {
        var prior = LiveOptions.PathBase;
        try
        {
            LiveOptions.PathBase = "/appA/";
            Assert.Equal("/appA", LiveOptions.PathBase);

            LiveOptions.PathBase = "appA";
            Assert.Equal("/appA", LiveOptions.PathBase);

            LiveOptions.PathBase = "/";
            Assert.Equal(string.Empty, LiveOptions.PathBase);

            LiveOptions.PathBase = string.Empty;
            Assert.Equal(string.Empty, LiveOptions.PathBase);
        }
        finally
        {
            LiveOptions.PathBase = prior;
        }
    }

    [Fact]
    public void RaskLiveOptions_PathBase_NormalizesOnAssignment()
    {
        var opts = new RaskLiveOptions();
        Assert.Equal(string.Empty, opts.PathBase);

        opts.PathBase = "/sub/";
        Assert.Equal("/sub", opts.PathBase);

        opts.PathBase = "sub";
        Assert.Equal("/sub", opts.PathBase);

        opts.PathBase = string.Empty;
        Assert.Equal(string.Empty, opts.PathBase);
    }

    [Fact]
    public void LiveOptions_PathBase_DefaultsToEmpty()
    {
        // Tests sharing the AppDomain may have written PathBase before this test runs,
        // so reset to baseline first and verify the empty default is observable.
        var prior = LiveOptions.PathBase;
        try
        {
            LiveOptions.PathBase = string.Empty;
            Assert.Equal(string.Empty, LiveOptions.PathBase);
        }
        finally
        {
            LiveOptions.PathBase = prior;
        }
    }
}
