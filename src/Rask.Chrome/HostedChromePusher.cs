using System.Text;
using System.Text.Json;
using Rask.Chrome.Components;
using Rask.Core;

namespace Rask.Chrome;

/// <summary>
///     The chrome half of a hosted session: collect the bars a frame composed, describe them, and hand the
///     description to a native shell — once per actual change.
/// </summary>
/// <remarks>
///     <para>
///         Shared by the Server and WASM hosts because their job here is the same to the byte. What differs
///         between them is only how a string reaches the page, and that is the one thing this asks for.
///     </para>
///     <para>
///         A hosted app can only describe the portable bars. <c>NativeHeaderBar</c> and <c>NativeToolbar</c>
///         live in Rask.Native, which a server app does not reference and cannot name — so <c>AppBar</c> and
///         <c>TabStrip</c> are the entire vocabulary on this side, which is precisely why they exist.
///     </para>
/// </remarks>
internal sealed class HostedChromePusher
{
    private Component? _header;
    private Component? _footer;
    private byte[]? _lastPushed;
    private Dictionary<string, Action> _taps = new(StringComparer.Ordinal);

    /// <summary>Note a component the render walk reported, if it is a bar this can describe.</summary>
    public void Collect(Component component)
    {
        switch (component)
        {
            case AppBar:
                _header = component;
                break;
            case TabStrip:
                _footer = component;
                break;
        }
    }

    /// <summary>Forget the previous frame's bars, so one removed from the tree actually disappears.</summary>
    public void Reset()
    {
        _header = null;
        _footer = null;
    }

    /// <summary>
    ///     Describe this frame's bars and send them, unless they are byte-identical to what the shell is
    ///     already showing.
    /// </summary>
    /// <param name="currentPath">The route, so a tab strip knows which tab is selected.</param>
    /// <param name="send">Delivers the descriptor JSON to the shell.</param>
    /// <returns>Whether anything was sent.</returns>
    /// <remarks>
    ///     The unchanged check is not an optimisation. A platform bar reapplied on every keystroke visibly
    ///     flickers, and on iOS it cuts off the navigation title's own animation mid-flight.
    /// </remarks>
    public bool Push(string currentPath, Action<string> send)
    {
        ArgumentNullException.ThrowIfNull(send);

        var handlers = new Dictionary<string, Action>(StringComparer.Ordinal);
        var descriptor = new NativeChromeDescriptor
        {
            Header = ChromeDescriptorBuilder.BuildHeader(_header, handlers),
            Footer = ChromeDescriptorBuilder.BuildFooter(_footer, currentPath),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            descriptor, NativeChromeJsonContext.Default.NativeChromeDescriptor);

        if (_lastPushed is not null && bytes.AsSpan().SequenceEqual(_lastPushed))
        {
            return false;
        }

        _lastPushed = bytes;
        // Swapped only as the descriptor goes out, so a tap always resolves against the bar the user is
        // actually looking at rather than one this frame has already replaced.
        _taps = handlers;

        send(Encoding.UTF8.GetString(bytes));
        return true;
    }

    /// <summary>Forget what the shell is showing, so the next push sends even if nothing changed.</summary>
    /// <remarks>
    ///     For the moment a shell starts listening. The descriptor built during the initial render has
    ///     nowhere to go yet — the transport does not exist — and without this the unchanged-check would
    ///     then suppress the very first delivery, leaving the app with no bars until the user happened to
    ///     do something that changed them.
    /// </remarks>
    public void Invalidate() => _lastPushed = null;

    /// <summary>Run the callback behind a bar item the user pressed.</summary>
    /// <returns>
    ///     Whether the id matched. An id from a bar since replaced simply does nothing — the press raced
    ///     the swap, and that is not an error.
    /// </returns>
    public bool TryRunTap(string? id) =>
        !string.IsNullOrEmpty(id) && _taps.TryGetValue(id, out var handler) && Run(handler);

    private static bool Run(Action handler)
    {
        handler();
        return true;
    }
}
