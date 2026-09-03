using System.Text;

namespace Rask.Wire.Tests;

public sealed class RemoteFileTests
{
    [Fact]
    public void FromStream_opens_the_source_lazily_and_only_when_read()
    {
        var opened = 0;
        var file = RemoteFile.FromStream("a.txt", "text/plain", 3, _ =>
        {
            opened++;
            return new MemoryStream("abc"u8.ToArray());
        });

        Assert.Equal(0, opened);

        using var stream = file.OpenReadStream();

        Assert.Equal(1, opened);
        Assert.Equal("abc", new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public void FromStream_defaults_a_missing_content_type_rather_than_sending_an_empty_one()
    {
        Assert.Equal("application/octet-stream", RemoteFile.FromStream("a", null, 0, _ => Stream.Null).ContentType);
        Assert.Equal("application/octet-stream", RemoteFile.FromStream("a", "", 0, _ => Stream.Null).ContentType);
        Assert.Equal("text/csv", RemoteFile.FromStream("a", "text/csv", 0, _ => Stream.Null).ContentType);
    }

    [Fact]
    public void A_negative_size_normalises_to_UnknownSize_so_callers_have_one_value_to_check()
    {
        Assert.Equal(RemoteFile.UnknownSize, RemoteFile.FromStream("a", null, -7, _ => Stream.Null).Size);
        Assert.Equal(RemoteFile.UnknownSize, RemoteFile.FromStream("a", null, RemoteFile.UnknownSize, _ => Stream.Null).Size);
        Assert.Equal(0, RemoteFile.FromStream("a", null, 0, _ => Stream.Null).Size);
    }

    [Fact]
    public void FromBytes_reports_the_length_it_actually_carries()
    {
        var bytes = Encoding.UTF8.GetBytes("hello");
        var file = RemoteFile.FromBytes("greeting.txt", "text/plain", bytes);

        Assert.Equal(5, file.Size);
        Assert.Equal("greeting.txt", file.Name);

        using var stream = file.OpenReadStream();
        Assert.Equal("hello", new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public void The_cancellation_token_reaches_the_source()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;

        var file = RemoteFile.FromStream("a", null, 0, ct =>
        {
            seen = ct;
            return Stream.Null;
        });

        file.OpenReadStream(cts.Token).Dispose();

        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public void LastModified_is_null_unless_supplied()
    {
        var when = new DateTimeOffset(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

        Assert.Null(RemoteFile.FromStream("a", null, 0, _ => Stream.Null).LastModified);
        Assert.Equal(when, RemoteFile.FromStream("a", null, 0, _ => Stream.Null, when).LastModified);
    }

    [Fact]
    public void A_file_without_a_name_or_a_source_is_rejected_at_construction()
    {
        Assert.Throws<ArgumentException>(() => RemoteFile.FromStream("", null, 0, _ => Stream.Null));
        Assert.Throws<ArgumentNullException>(() => RemoteFile.FromStream(null!, null, 0, _ => Stream.Null));
        Assert.Throws<ArgumentNullException>(() => RemoteFile.FromStream("a", null, 0, null!));
        Assert.Throws<ArgumentNullException>(() => RemoteFile.FromBytes("a", null, null!));
    }
}
