using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;

namespace Rask.Printing;

/// <summary>
/// The Chrome DevTools protocol over <c>--remote-debugging-pipe</c>: one JSON message per command and per reply,
/// each ended by a NUL byte. A reply finds its command by id; an event goes to the listener of its session.
/// </summary>
internal sealed class DevToolsConnection : IAsyncDisposable
{
    private readonly Stream _toBrowser;
    private readonly PipeReader _fromBrowser;
    private readonly Func<string> _browserLog;
    private readonly SemaphoreSlim _writing = new(1, 1);
    private readonly ConcurrentDictionary<int, (string Method, TaskCompletionSource<JsonElement> Reply)> _pending = new();
    private readonly ConcurrentDictionary<(string Session, string Method), Action<JsonElement>> _listeners = new();
    private readonly Task _reading;
    private int _lastId;
    private int _disposed;
    private volatile Exception? _closed;

    /// <summary>Starts reading what Chromium writes; commands go out as they are sent.</summary>
    /// <param name="toBrowser">The stream Chromium reads commands from (its fd 3).</param>
    /// <param name="fromBrowser">The stream Chromium writes replies and events to (its fd 4).</param>
    /// <param name="browserLog">The last lines Chromium wrote to stderr, for the message when it goes away.</param>
    public DevToolsConnection(Stream toBrowser, Stream fromBrowser, Func<string> browserLog)
    {
        _toBrowser = toBrowser;
        _fromBrowser = PipeReader.Create(fromBrowser);
        _browserLog = browserLog;
        _reading = Task.Run(Read);
    }

    /// <summary>Whether the pipe is gone — Chromium exited, crashed or was closed.</summary>
    public bool IsClosed => _closed is not null;

    /// <summary>Sends a command (to the browser, or to the page of <paramref name="sessionId"/>) and returns its result.</summary>
    public async Task<JsonElement> Send(
        string method, Action<Utf8JsonWriter>? parameters = null, string? sessionId = null, CancellationToken cancellation = default)
    {
        var id = Interlocked.Increment(ref _lastId);
        var reply = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = (method, reply);
        if (_closed is { } closed && _pending.TryRemove(id, out _))
        {
            throw new IOException(closed.Message, closed);
        }

        using var cancel = cancellation.Register(() =>
        {
            if (_pending.TryRemove(id, out var waiting))
            {
                waiting.Reply.TrySetCanceled(cancellation);
            }
        });

        try
        {
            await Write(Message(id, method, parameters, sessionId), cancellation).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }

        return await reply.Task.ConfigureAwait(false);
    }

    /// <summary>Hands every <paramref name="method"/> event of the session to <paramref name="listener"/>, on the reading thread.</summary>
    /// <remarks>The element lives only for the call: a listener copies what it keeps.</remarks>
    public void Listen(string sessionId, string method, Action<JsonElement> listener) =>
        _listeners[(sessionId, method)] = listener;

    /// <summary>Drops every listener of a session that is over.</summary>
    public void Forget(string sessionId)
    {
        foreach (var key in _listeners.Keys.Where(key => string.Equals(key.Session, sessionId, StringComparison.Ordinal)))
        {
            _listeners.TryRemove(key, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _fromBrowser.CancelPendingRead();
        await _reading.ConfigureAwait(false);
        await _fromBrowser.CompleteAsync().ConfigureAwait(false);
        await _toBrowser.DisposeAsync().ConfigureAwait(false);
        _writing.Dispose();
    }

    private static ReadOnlyMemory<byte> Message(int id, string method, Action<Utf8JsonWriter>? parameters, string? sessionId)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("id", id);
            json.WriteString("method", method);
            if (sessionId is not null)
            {
                json.WriteString("sessionId", sessionId);
            }

            json.WriteStartObject("params");
            parameters?.Invoke(json);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        buffer.Write([(byte)0]);
        return buffer.WrittenMemory;
    }

    // A message is written whole or not at all: cancelling halfway would leave Chromium half a command, and the
    // pipe would be unreadable from there on. Waiting for the turn to write is what cancels.
    private async Task Write(ReadOnlyMemory<byte> message, CancellationToken cancellation)
    {
        await _writing.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            await _toBrowser.WriteAsync(message, CancellationToken.None).ConfigureAwait(false);
            await _toBrowser.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    private async Task Read()
    {
        Exception why;
        try
        {
            while (true)
            {
                var result = await _fromBrowser.ReadAsync().ConfigureAwait(false);
                var buffer = result.Buffer;
                while (TryTakeMessage(ref buffer, out var message))
                {
                    Dispatch(message);
                }

                _fromBrowser.AdvanceTo(buffer.Start, buffer.End);
                if (result.IsCompleted || result.IsCanceled)
                {
                    break;
                }
            }

            why = new IOException("Chromium closed its DevTools pipe: it exited or crashed." + _browserLog());
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or JsonException or InvalidOperationException)
        {
            why = new IOException("Chromium's DevTools pipe broke." + _browserLog(), e);
        }

        Close(why);
    }

    private static bool TryTakeMessage(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> message)
    {
        var reader = new SequenceReader<byte>(buffer);
        if (!reader.TryReadTo(out message, 0))
        {
            return false;
        }

        buffer = buffer.Slice(reader.Position);
        return true;
    }

    private void Dispatch(ReadOnlySequence<byte> message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        if (root.TryGetProperty("id", out var id))
        {
            if (_pending.TryRemove(id.GetInt32(), out var waiting))
            {
                Answer(waiting.Method, waiting.Reply, root);
            }

            return;
        }

        var session = root.TryGetProperty("sessionId", out var sessionId) ? sessionId.GetString() ?? string.Empty : string.Empty;
        if (root.TryGetProperty("method", out var method)
            && _listeners.TryGetValue((session, method.GetString() ?? string.Empty), out var listener)
            && root.TryGetProperty("params", out var parameters))
        {
            listener(parameters);
        }
    }

    private static void Answer(string method, TaskCompletionSource<JsonElement> reply, JsonElement root)
    {
        if (root.TryGetProperty("error", out var error))
        {
            var why = error.TryGetProperty("message", out var text) ? text.GetString() : error.GetRawText();
            reply.TrySetException(new InvalidOperationException($"Chromium refused {method}: {why}"));
            return;
        }

        reply.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
    }

    private void Close(Exception why)
    {
        _closed = why;
        _listeners.Clear();
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var waiting))
            {
                waiting.Reply.TrySetException(why);
            }
        }
    }
}
