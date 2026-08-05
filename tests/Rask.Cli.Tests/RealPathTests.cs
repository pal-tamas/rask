namespace Rask.Cli.Tests;

/// <summary>
///     <see cref="RealPath.Resolve" /> exists for one reason: a project path that traverses a symlink
///     makes <c>dotnet watch</c> compute an empty hot-reload delta, silently (#536). These pin the
///     behaviour that matters for that — an ancestor link is followed, and nothing else is disturbed.
/// </summary>
public sealed class RealPathTests : IDisposable
{
    // Resolved up front, because the temp root is itself symlinked on macOS — the very condition under
    // test. Expectations built from an unresolved root would compare /var/… against /private/var/….
    private readonly string _root = RealPath.Resolve(
        Path.Combine(Path.GetTempPath(), "rask-realpath-" + Guid.NewGuid().ToString("N")));

    public RealPathTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void A_link_in_an_ancestor_is_followed_not_just_the_last_segment()
    {
        // The shape that actually bites: the link is a directory several levels above the file, exactly
        // like /var → /private/var with the project sitting under /var/folders/…. FileSystemInfo
        // .ResolveLinkTarget alone would miss this, because it only resolves the entry it is given.
        var real = Directory.CreateDirectory(Path.Combine(_root, "real", "nested")).FullName;
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, Path.Combine(_root, "real"));

        var resolved = RealPath.Resolve(Path.Combine(link, "nested", "App.csproj"));

        Assert.Equal(Path.Combine(real, "App.csproj"), resolved);
    }

    [Fact]
    public void GetFullPath_alone_would_not_have_been_enough()
    {
        // Guards the specific wrong turn that made this look ruled out for months: GetFullPath normalises
        // separators and `..` but never follows a symlink, so it returns the path unchanged and reads as
        // a negative result.
        Directory.CreateDirectory(Path.Combine(_root, "real"));
        var link = Path.Combine(_root, "link");
        Directory.CreateSymbolicLink(link, Path.Combine(_root, "real"));

        var target = Path.Combine(link, "App.csproj");

        Assert.Equal(target, Path.GetFullPath(target));
        Assert.NotEqual(target, RealPath.Resolve(target));
    }

    [Fact]
    public void Segments_that_do_not_exist_yet_are_kept_verbatim()
    {
        // The harness resolves its temp root before creating it, so this must not throw or truncate.
        var path = Path.Combine(_root, "not-created-yet", "deeper", "App.csproj");

        Assert.Equal(path, RealPath.Resolve(path));
    }

    [Fact]
    public void A_path_with_no_links_is_only_normalised()
    {
        var plain = Directory.CreateDirectory(Path.Combine(_root, "plain")).FullName;

        Assert.Equal(
            Path.Combine(plain, "App.csproj"),
            RealPath.Resolve(Path.Combine(plain, ".", "sub", "..", "App.csproj")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Empty_input_is_returned_unchanged(string? path) =>
        Assert.Equal(path, RealPath.Resolve(path!));
}
