using Rask.Native.Files;

namespace Rask.Native.Tests.Files;

// A download name is only a suggestion in a browser, which sanitizes it before anything touches a
// filesystem. On a native head it becomes a real path — and the value can be attacker-influenced (a record
// title, a filename echoed back from an API) — so the reduction to a single safe segment is load-bearing.
public sealed class NativeDownloadStagingTests
{
    [Theory]
    // Traversal, on both platforms' separators. The backslash cases matter even on Unix: Path.GetFileName is
    // platform-aware and would pass them through intact here, leaving a name that only becomes a traversal
    // once a Windows-rules head joins it.
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\config", "config")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("C:\\secrets\\key.pem", "key.pem")]
    // Bare traversal segments survive every character filter yet name no file.
    [InlineData("..", "download")]
    [InlineData(".", "download")]
    [InlineData("", "download")]
    [InlineData("   ", "download")]
    [InlineData(null, "download")]
    // Windows silently drops trailing dots and spaces, so keeping them would mean the file on disk had a
    // different name than the one reported to the user.
    [InlineData("report.txt.", "report.txt")]
    [InlineData("report.txt  ", "report.txt")]
    // Ordinary names are left alone, including spaces and unicode.
    [InlineData("Q3 report (final).pdf", "Q3 report (final).pdf")]
    [InlineData("jelentés.pdf", "jelentés.pdf")]
    public void SafeFileName_ReducesToASinglePathSegment(string? input, string expected) =>
        Assert.Equal(expected, NativeDownloadStaging.SafeFileName(input));

    [Fact]
    public void SafeFileName_ReplacesControlCharacters()
    {
        // A newline in a name would also break the platform's own presentation of it.
        Assert.Equal("a_b", NativeDownloadStaging.SafeFileName("a\nb"));
        Assert.Equal("a_b", NativeDownloadStaging.SafeFileName("a\0b"));
    }

    [Fact]
    public async Task StageAsync_WritesTheBytesUnderTheDownloadDirectory()
    {
        var file = await NativeDownloadStaging.StageAsync("notes.txt", "text/plain", "hello"u8.ToArray());

        Assert.Equal("notes.txt", file.FileName);
        Assert.Equal("text/plain", file.ContentType);
        Assert.StartsWith(
            Path.Combine(Path.GetTempPath(), "rask-downloads"), file.Path, StringComparison.Ordinal);
        Assert.Equal("hello", await File.ReadAllTextAsync(file.Path));
    }

    [Fact]
    public async Task StageAsync_GivesEachDownloadItsOwnDirectory_SoSameNamedFilesDoNotCollide()
    {
        var first = await NativeDownloadStaging.StageAsync("a.txt", "text/plain", "one"u8.ToArray());
        var second = await NativeDownloadStaging.StageAsync("a.txt", "text/plain", "two"u8.ToArray());

        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal("one", await File.ReadAllTextAsync(first.Path));
        Assert.Equal("two", await File.ReadAllTextAsync(second.Path));
    }

    [Fact]
    public async Task StageAsync_DefaultsTheContentType_WhenTheCallerGaveNone()
    {
        var file = await NativeDownloadStaging.StageAsync("blob.bin", null, [1, 2, 3]);

        Assert.Equal("application/octet-stream", file.ContentType);
    }
}
