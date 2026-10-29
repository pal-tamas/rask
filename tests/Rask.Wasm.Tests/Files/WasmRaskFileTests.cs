using Rask.Wasm.Files;

namespace Rask.Wasm.Tests.Files;

/// <summary>
///     A picked file read with no token of its own reads under the work in progress's — the handler that opened
///     it — so a page left mid-upload stops asking the browser for the next chunk.
/// </summary>
public class WasmRaskFileTests
{
    private static WasmRaskFile Picked() =>
        new("file-1", "notes.txt", size: 10, "text/plain", DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task A_stream_opened_with_no_token_is_cancelled_with_the_work_that_opened_it()
    {
        using var cts = new CancellationTokenSource();
        Stream stream;
        using (Ambient.Enter(cts.Token))
        {
#pragma warning disable xUnit1051 // the missing token is the point: the stream keeps the one it opened under
            stream = Picked().OpenReadStream();
#pragma warning restore xUnit1051
        }

        await cts.CancelAsync();

        await using (stream)
        {
#pragma warning disable CA2016, xUnit1051 // the missing token is the point: the stream keeps the one it opened under
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.ReadExactlyAsync(new byte[4]));
#pragma warning restore CA2016, xUnit1051
        }
    }

    [Fact]
    public async Task A_read_given_its_own_token_is_cancelled_by_it()
    {
        await using var stream = Picked().OpenReadStream(cancellationToken: TestContext.Current.CancellationToken);
        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.ReadExactlyAsync(new byte[4], cts.Token));
    }
}
