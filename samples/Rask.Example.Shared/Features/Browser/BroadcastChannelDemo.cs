using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IBroadcastChannel" /> — same-origin messaging between browsing contexts. This demo opens
///     two connections to one channel in the same page: posting on the sender is delivered to the receiver
///     (a connection never receives its own posts). Open this page in a second tab to see cross-tab
///     delivery. The receiver updates state in its handler and calls <c>StateHasChanged()</c> — the
///     sanctioned pattern for an externally-pushed update (same as subscribing to a background feed).
/// </summary>
public sealed class BroadcastChannelDemo(IBroadcastChannel bus) : Component, IAsyncDisposable
{
    private const string ChannelName = "rask-broadcast-demo";
    private IBroadcastChannelConnection? _sender;
    private IBroadcastChannelConnection? _receiver;
    private readonly List<string> _received = [];
    private int _counter;
    private bool _opened;

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender || _opened)
        {
            return;
        }

        _opened = true;
        _sender = await bus.OpenAsync(ChannelName, _ => Task.CompletedTask);
        _receiver = await bus.OpenAsync(ChannelName, msg =>
        {
            _received.Insert(0, msg);
            StateHasChanged();
            return Task.CompletedTask;
        });
    }

    protected override RenderResult Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                BsButton(Color: BsColor.Primary, Size: BsSize.Sm, Class: "mb-2", Id: "bc-send", OnClickAsync: Send)["Broadcast a message"],
                Div(Class: "small text-secondary mb-1")["Received (from other connections/tabs):"],
                _received.Count == 0
                    ? Div(Class: "small text-secondary fst-italic", Id: "bc-log")["(none yet)"]
                    : Ul(Class: "small mb-0", Id: "bc-log")[
                        _received.Select(m => Li(Key: m)[m])
                    ]
            ]
        ];

    private async Task Send()
    {
        if (_sender is null)
        {
            return;
        }

        await _sender.PostAsync($"Message #{++_counter}");
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender is not null)
        {
            await _sender.DisposeAsync();
        }

        if (_receiver is not null)
        {
            await _receiver.DisposeAsync();
        }
    }
}
