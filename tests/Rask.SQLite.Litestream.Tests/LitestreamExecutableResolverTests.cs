namespace Rask.SQLite.Litestream.Tests;

public sealed class LitestreamExecutableResolverTests
{
    [Fact]
    public void Resolve_returns_an_absolute_path_verbatim()
    {
        var path = OperatingSystem.IsWindows() ? @"C:\tools\litestream.exe" : "/usr/local/bin/litestream";
        Assert.Equal(path, LitestreamExecutableResolver.Resolve(path));
    }

    [Fact]
    public void Resolve_returns_a_relative_path_with_a_separator_verbatim()
    {
        Assert.Equal("tools/litestream", LitestreamExecutableResolver.Resolve("tools/litestream"));
    }

    [Fact]
    public void Resolve_falls_back_to_the_bare_name_when_nothing_is_bundled()
    {
        // A name that isn't present next to the app resolves unchanged, for a normal PATH lookup.
        Assert.Equal("litestream-not-bundled", LitestreamExecutableResolver.Resolve("litestream-not-bundled"));
    }

    [Fact]
    public void Resolve_prefers_a_binary_bundled_next_to_the_app()
    {
        var probeName = $"litestream-probe-{Guid.NewGuid():N}";
        var bundledPath = Path.Combine(AppContext.BaseDirectory, probeName);
        File.WriteAllText(bundledPath, "stub");
        try
        {
            Assert.Equal(bundledPath, LitestreamExecutableResolver.Resolve(probeName));
        }
        finally
        {
            File.Delete(bundledPath);
        }
    }
}
