namespace Rask.Cli.Tests;

/// <summary>
/// The sizes <c>.rask/deploy.json</c> takes (<c>"buildCacheKeep"</c>, <c>"minFreeDisk"</c>, <c>"warnFreeDisk"</c>) are
/// read the way docker reads <c>--reserved-space 20GB</c>: powers of 1024, with or without the <c>i</c>.
/// </summary>
public sealed class ByteSizeTests
{
    private const long Mebibyte = 1024L * 1024;
    private const long Gibibyte = 1024L * Mebibyte;

    [Theory]
    [InlineData("20GB", 20 * Gibibyte)]
    [InlineData("3GiB", 3 * Gibibyte)]
    [InlineData("3G", 3 * Gibibyte)]
    [InlineData("20 gb", 20 * Gibibyte)]
    [InlineData("500MB", 500 * Mebibyte)]
    [InlineData("1.5GB", 1536 * Mebibyte)]
    [InlineData("2TB", 2048 * Gibibyte)]
    [InlineData("1024", 1024L)]
    [InlineData("0", 0L)]
    public void A_size_is_read_in_powers_of_1024_as_docker_reads_it(string text, long expected)
    {
        var given = text;

        var read = ByteSize.TryParse("buildCacheKeep", given, out var bytes, out var error);

        Assert.True(read, error);
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("-3GB")]
    [InlineData("lots")]
    [InlineData("")]
    [InlineData("3XB")]
    [InlineData("1.5.5GB")]
    [InlineData("GB")]
    public void A_negative_or_garbage_size_is_refused_with_how_to_write_one(string text)
    {
        var given = text;

        var read = ByteSize.TryParse("minFreeDisk", given, out _, out var error);

        Assert.False(read);
        Assert.Equal($"\"minFreeDisk\": \"{text}\" isn't a size — write it like \"20GB\", \"500MB\" or \"3GiB\".", error);
    }

    [Fact]
    public void A_size_past_a_petabyte_is_refused_as_out_of_range()
    {
        var given = "2000PB";

        var read = ByteSize.TryParse("warnFreeDisk", given, out _, out var error);

        Assert.False(read);
        Assert.Equal("\"warnFreeDisk\": \"2000PB\" is out of range — at most 1PB, written like \"20GB\".", error);
    }

    [Theory]
    [InlineData(20 * Gibibyte, "20GB")]
    [InlineData(1536 * Mebibyte, "1536MB")]
    [InlineData(1000L, "1000B")]
    [InlineData(0L, "0B")]
    public void A_size_is_written_in_the_largest_unit_that_holds_it_whole_and_reads_back_the_same(long bytes, string expected)
    {
        var size = bytes;

        var written = ByteSize.Format(size);
        ByteSize.TryParse("buildCacheKeep", written, out var back, out _);

        Assert.Equal(expected, written);
        Assert.Equal(bytes, back);
    }
}
