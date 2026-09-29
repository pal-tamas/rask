using System.Text;
using Rask.Batteries;

namespace Rask.Storage.Tests;

/// <summary>The fake stands in for the whole store, so there is no disk, no bucket and no database.</summary>
public sealed class FilesFakeTests
{
    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task A_fake_takes_every_save_instead_of_the_real_store()
    {
        using var files = Files.Fake();

        await Files.Save(Bytes("hello"), "note.txt", TestContext.Current.CancellationToken);

        files.Saved().Named("note.txt").Once();
    }

    [Fact]
    public async Task A_public_save_is_told_apart_from_a_private_one()
    {
        using var files = Files.Fake();

        await Files.Save(Bytes("avatar"), "avatar.png", TestContext.Current.CancellationToken).Public();
        await Files.Save(Bytes("invoice"), "invoice.pdf", TestContext.Current.CancellationToken);

        files.Saved().Public().Named("avatar.png").Once();
        files.Saved().Private().Named("invoice.pdf").Once();
        files.Saved().Public().Named("invoice.pdf").None();
    }

    [Fact]
    public async Task What_was_saved_is_read_back()
    {
        using var files = Files.Fake();

        var saved = await Files.Save(Bytes("hello"), "note.txt", TestContext.Current.CancellationToken);

        var row = await Files.Get(saved.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(row);
        Assert.Equal("note.txt", row.Name);
        Assert.Equal("hello", Encoding.UTF8.GetString(files.Bytes(saved.Id)!));
    }

    [Fact]
    public async Task A_deleted_file_is_gone_and_recorded_as_deleted()
    {
        using var files = Files.Fake();
        var saved = await Files.Save(Bytes("hello"), "note.txt", TestContext.Current.CancellationToken);

        Assert.True(await Files.Delete(saved.Id, TestContext.Current.CancellationToken));

        Assert.Null(await Files.Get(saved.Id, TestContext.Current.CancellationToken));
        files.Deleted(saved.Id).Once();
    }

    [Fact]
    public async Task A_share_link_is_made_for_a_file_that_exists_and_not_for_one_that_does_not()
    {
        using var files = Files.Fake();
        var saved = await Files.Save(Bytes("invoice"), "invoice.pdf", TestContext.Current.CancellationToken);

        Assert.NotNull(await Files.Share(saved.Id, TestContext.Current.CancellationToken).For(15.Minutes));
        Assert.Null(await Files.Share(Guid.NewGuid(), TestContext.Current.CancellationToken).For(15.Minutes));
    }

    [Fact]
    public async Task A_stream_is_opened_over_what_was_saved()
    {
        using var files = Files.Fake();
        var saved = await Files.Save(Bytes("hello"), "note.txt", TestContext.Current.CancellationToken);

        await using var stream = await Files.OpenRead(saved.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        Assert.Equal("hello", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failure_names_what_was_saved_instead()
    {
        using var files = Files.Fake();

        await Files.Save(Bytes("hello"), "note.txt", TestContext.Current.CancellationToken);

        var error = Assert.Throws<CountingException>(() => files.Saved().Named("avatar.png").Once());
        Assert.Contains("\"note.txt\"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disposing_the_fake_puts_the_real_store_back()
    {
        using (var files = Files.Fake())
        {
            await Files.Save(Bytes("hello"), "note.txt", TestContext.Current.CancellationToken);
            files.Saved().Once();
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Files.Save(Bytes("hello"), "note.txt", TestContext.Current.CancellationToken));
        Assert.Contains("Inject IFiles", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_injected_IFiles_can_be_the_fake_too()
    {
        using var files = Files.Fake();
        IFiles injected = files;

        await injected.Save(Bytes("hello"), "note.txt", TestContext.Current.CancellationToken).Public();

        files.Saved().Public().Once();
    }
}
