using System.Text.Json;
using Rask.Core.Forms;
using Rask.Wasm.Files;

namespace Rask.Wasm.Tests.Files;

/// <summary>
///     The reader's cancel of an upload in the browser host: it cancels the handler receiving those files — or the one
///     about to — and no other.
/// </summary>
public class WasmUploadCancelsTests
{
    private static IRaskFile Picked(WasmFileBackend backend, string @ref)
    {
        using var meta = JsonDocument.Parse($$"""{"ref":"{{@ref}}","name":"{{@ref}}.png","size":10,"type":"image/png","lastModified":0}""");
        return backend.Create(meta.RootElement);
    }

    private static string Unique() => Guid.NewGuid().ToString("N");

    [Fact]
    public void A_cancel_cancels_the_handler_reading_those_files()
    {
        var backend = new WasmFileBackend();
        var one = Unique();
        var files = new[] { Picked(backend, one) };
        var token = backend.CancellationOf(files);

        WasmUploadCancels.Cancel([one]);

        Assert.True(token.IsCancellationRequested);
        backend.Release(files);
    }

    [Fact]
    public void A_cancel_of_one_upload_leaves_another_running()
    {
        var backend = new WasmFileBackend();
        var (one, other) = (Unique(), Unique());
        var mine = new[] { Picked(backend, one) };
        var theirs = new[] { Picked(backend, other) };
        var cancelled = backend.CancellationOf(mine);
        var running = backend.CancellationOf(theirs);

        WasmUploadCancels.Cancel([one]);

        Assert.True(cancelled.IsCancellationRequested);
        Assert.False(running.IsCancellationRequested);
        backend.Release(mine);
        backend.Release(theirs);
    }

    [Fact]
    public void A_cancel_that_arrives_before_the_handler_starts_it_cancelled()
    {
        var backend = new WasmFileBackend();
        var early = Unique();
        WasmUploadCancels.Cancel([early]);

        var token = backend.CancellationOf([Picked(backend, early)]);

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public void Files_chosen_again_after_a_cancel_start_with_nothing_called_off()
    {
        var backend = new WasmFileBackend();
        var first = Unique();
        var files = new[] { Picked(backend, first) };
        backend.CancellationOf(files);
        WasmUploadCancels.Cancel([first]);
        backend.Release(files);

        var again = backend.CancellationOf([Picked(backend, Unique())]);

        Assert.False(again.IsCancellationRequested);
    }

    [Fact]
    public void A_cancel_pressed_after_the_handler_was_done_changes_nothing()
    {
        var backend = new WasmFileBackend();
        var done = Unique();
        var files = new[] { Picked(backend, done) };
        var token = backend.CancellationOf(files);
        backend.Release(files);

        var cancel = Record.Exception(() => WasmUploadCancels.Cancel([done]));

        Assert.Null(cancel);
        Assert.False(token.CanBeCanceled && token.IsCancellationRequested);
    }
}
