using System.Net;

namespace Rask.Storage.Tests;

/// <summary>
///     <c>Save(…).As("application/pdf")</c>: the caller states the content type, and it is recorded and served in place
///     of the sniffed one. Unset, the bytes still decide.
/// </summary>
[Collection(StorageDbCollection.Name)]
public sealed class StatedContentTypeTests
{
    [Fact]
    public async Task A_stated_content_type_is_recorded_in_place_of_the_sniffed_one()
    {
        await using var harness = new StorageHarness();

        var file = await harness.Files.Save(new MemoryStream(Samples.Text), "bevallas.xml", TestContext.Current.CancellationToken)
            .As("application/xml");

        Assert.Equal("application/xml", file.ContentType);
        Assert.Equal("application/xml", (await harness.Files.Get(file.Id, TestContext.Current.CancellationToken))!.ContentType);
    }

    [Fact]
    public async Task Without_a_stated_type_the_bytes_still_decide()
    {
        await using var harness = new StorageHarness();

        var file = await harness.Files.Save(new MemoryStream(Samples.Text), "bevallas.xml", TestContext.Current.CancellationToken);

        Assert.Equal("text/plain", file.ContentType);
    }

    [Fact]
    public async Task A_stated_content_type_is_served_by_a_download_and_by_a_temporary_url()
    {
        await using var host = await FileHost.StartAsync();
        var file = await host.Files.Save(new MemoryStream(Samples.Text), "report.pdf", TestContext.Current.CancellationToken)
            .As("application/pdf");
        var link = await host.Files.Share(file.Id, TestContext.Current.CancellationToken).For(TimeSpan.FromMinutes(5));

        using var download = await host.Client.GetAsync($"/download/{file.Id}", TestContext.Current.CancellationToken);
        using var shared = await host.Client.GetAsync(link, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.Equal("application/pdf", shared.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_stated_type_that_can_run_script_is_still_served_as_an_opaque_download()
    {
        await using var host = await FileHost.StartAsync();
        var file = await host.Files.Save(new MemoryStream(Samples.Text), "bevallas.xml", TestContext.Current.CancellationToken)
            .As("application/xml");

        using var download = await host.Client.GetAsync($"/download/{file.Id}", TestContext.Current.CancellationToken);

        Assert.Equal("application/xml", file.ContentType);
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Theory]
    [InlineData("application/xml\r\nSet-Cookie: session=stolen")]
    [InlineData("application/xml\nX: y")]
    [InlineData("xml")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("application/")]
    [InlineData("/xml")]
    [InlineData("application/xml/extra")]
    [InlineData("application/xml; charset=utf-8")]
    [InlineData("application/*")]
    [InlineData("text/ plain")]
    public async Task A_content_type_that_is_not_a_plain_media_type_is_refused(string contentType)
    {
        await using var harness = new StorageHarness();
        var saving = harness.Files.Save(new MemoryStream(Samples.Text), "bevallas.xml", TestContext.Current.CancellationToken);

        Assert.Throws<ArgumentException>(() => saving.As(contentType));

        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task A_stated_type_outside_the_allowed_types_is_refused_and_nothing_is_stored()
    {
        await using var harness = new StorageHarness(o => o.AllowedTypes.Add("image/*"));

        var refused = await Assert.ThrowsAsync<FileRejectedException>(() => harness.Files
            .Save(new MemoryStream(Samples.Png()), "photo.png", TestContext.Current.CancellationToken)
            .As("application/pdf")
            .AsTask());

        Assert.Equal(FileRejection.TypeNotAllowed, refused.Reason);
        Assert.Empty(harness.StoredPaths());
        Assert.Equal(0, await harness.CountRowsAsync());
    }

    [Fact]
    public async Task A_fake_hands_back_the_stated_type()
    {
        using var files = Files.Fake();

        var saved = await Files.Save(new MemoryStream(Samples.Text), "bevallas.xml", TestContext.Current.CancellationToken)
            .As("application/xml");

        Assert.Equal("application/xml", saved.ContentType);
        Assert.Equal("application/xml", (await Files.Get(saved.Id, TestContext.Current.CancellationToken))!.ContentType);
    }
}
