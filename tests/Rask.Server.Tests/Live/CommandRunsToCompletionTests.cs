using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Cqrs;
using Rask.Querying;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-infra app predates framework-managed <head>

namespace Rask.Server.Tests.Live;

/// <summary>
///     A command once sent runs to its end on the services it began with, though the page that sent it has gone —
///     navigated away from, or its tab closed — as a request the visitor walked away from finishes on its server.
/// </summary>
public class CommandRunsToCompletionTests
{
    [Fact]
    public async Task A_command_a_hook_sent_runs_to_its_end_on_its_own_services_after_its_page_has_gone()
    {
        await using var page = await OpenAsync();
        ((EventServices)page.Session.EventServices).TeardownGrace = TimeSpan.Zero;
        await page.Navigate("/notes/draft");
        await page.Notes.Reached();

        await page.Navigate("/notes");
        await WaitFor.True(() => page.Notes.DraftLeft, "the draft page to leave");
        page.Notes.Open();

        await WaitFor.True(() => page.Notes.Finished, "the command to finish");
        Assert.Null(page.Notes.Failure);
        Assert.Equal(["draft"], page.Notes.Saved);
    }

    [Fact]
    public async Task A_command_whose_tab_closed_mid_write_runs_to_its_end_before_its_session_goes()
    {
        await using var page = await OpenAsync(o => o.SessionGracePeriod = TimeSpan.FromMilliseconds(300));
        await page.Click("save");
        await page.Notes.Reached();

        await page.Leave();
        await WaitFor.True(() => page.Host.Store.HasPendingRemoval(page.Session.Id), "the tab's grace to start");
        await WaitFor.True(() => !page.Host.Store.HasPendingRemoval(page.Session.Id), "the tab's grace to end");
        var stillThere = !page.Session.IsDisposed;
        page.Notes.Open();

        await WaitFor.True(() => page.Notes.Finished, "the command to finish");
        Assert.True(stillThere);
        Assert.Null(page.Notes.Failure);
        Assert.Equal(["clicked"], page.Notes.Saved);
        await WaitFor.True(() => page.Session.IsDisposed, "the session to go once the command is done");
    }

    private static RaskTestHost Host(Action<RaskServerOptions>? configure = null) =>
        RaskTestHost.Create<NotesApp>(
            services =>
            {
                services.AddRaskCqrs();
                services.AddRaskQuery();
                services.AddSingleton<NoteLedger>();
                services.AddScoped<NoteDb>();
            },
            configureServer: configure);

    private static async Task<OpenPage> OpenAsync(Action<RaskServerOptions>? configure = null)
    {
        var host = Host(configure);
        var html = await host.Http.GetStringAsync("/notes", TestContext.Current.CancellationToken);
        var session = host.Store.Peek(MarkupAssert.SessionId(html))!;
        var settled = session.AttachesSettled;
        var connection = await LiveTestConnection.OpenAsync(host, LiveTransportKind.WebSocket, session.Id);
        await WaitFor.True(() => session.AttachesSettled > settled, "the attach has adopted its connection's user");
        await session.LastHandlerTask;
        return new OpenPage(host, connection, session, html);
    }

    private sealed class OpenPage(RaskTestHost host, ILiveTestConnection connection, LiveSession session, string html)
        : IAsyncDisposable
    {
        private bool _left;

        public RaskTestHost Host => host;

        public LiveSession Session => session;

        public NoteLedger Notes => host.Server.Services.GetRequiredService<NoteLedger>();

        public Task Navigate(string path) => connection.SendJsonAsync(new { type = "navigate", path, query = "" });

        // Not settled: the handler is held mid-command, and its frame comes only once the test lets it go.
        public Task Click(string button)
        {
            var id = Regex.Match(html, "<button[^>]*data-rask-on-click=\"(h\\d+)\"[^>]*>" + button).Groups[1].Value;
            return connection.SendJsonAsync(new { id });
        }

        public async Task Leave()
        {
            _left = true;
            await connection.DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Notes.Open();
            if (!_left)
            {
                await connection.DisposeAsync();
            }

            await host.StopAsync();
            host.Dispose();
        }
    }
}

[Invalidates("notes")]
public sealed record SaveNote(string Text) : ICommand;

/// <summary>What the save writes, held at one point the test opens.</summary>
public sealed class NoteLedger
{
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _reached;

    public ConcurrentQueue<string> Saved { get; } = new();

    public Exception? Failure { get; set; }

    public bool Finished { get; set; }

    public bool DraftLeft { get; set; }

    public Task Reached() => WaitFor.True(() => _reached, "the save to reach its write");

    public void Open() => _gate.TrySetResult();

    // A write that honours the cancellation it runs under, as a SaveChangesAsync does.
    public async Task Write(NoteDb db, string text)
    {
        _reached = true;
        try
        {
            await _gate.Task.WaitAsync(Current.Cancellation);
            db.Write();
            Saved.Enqueue(text);
        }
        catch (Exception ex)
        {
            Failure = ex;
            throw;
        }
        finally
        {
            Finished = true;
        }
    }
}

// The app's DbContext: unusable once its scope is disposed.
public sealed class NoteDb : IDisposable
{
    private bool _disposed;

    public void Write() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose() => _disposed = true;
}

public sealed class SaveNoteHandler(NoteLedger ledger, NoteDb db) : ICommandHandler<SaveNote>
{
    public Task Handle(SaveNote command) => ledger.Write(db, command.Text);
}

public sealed partial class NotesApp : Component
{
    protected override Component? HeadAssets => Title["notes"];

    protected override Component? Render() => Div[Router];
}

[Route("/notes")]
public sealed partial class NotesHome(IQueryClient queries) : Component
{
    private readonly Command<SaveNote> _save = queries.Command<SaveNote>();

    protected override Component? Render() =>
        Button.OnClick(async () => await _save.Send(new SaveNote("clicked"), CancellationToken))["save"];
}

// Saves a draft as it opens, and is left while the save is still on its way.
[Route("/notes/draft")]
public sealed partial class NotesDraft(IQueryClient queries, NoteLedger ledger) : Component
{
    private readonly Command<SaveNote> _save = queries.Command<SaveNote>();
    private bool _sent;

    protected override async Task OnUpdated()
    {
        if (_sent)
        {
            return;
        }

        _sent = true;
        await _save.Send(new SaveNote("draft"), CancellationToken);
    }

    protected override Task OnUnmount()
    {
        ledger.DraftLeft = true;
        return Task.CompletedTask;
    }

    protected override Component? Render() => P["draft"];
}
