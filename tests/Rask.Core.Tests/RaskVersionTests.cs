using System.Text.RegularExpressions;

namespace Rask.Core.Tests;

public class RaskVersionTests
{
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
