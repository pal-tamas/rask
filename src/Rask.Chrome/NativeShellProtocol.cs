namespace Rask.Chrome;

/// <summary>
///     The handful of literals a native head and a hosted Rask app must agree on, byte for byte, to render
///     one app's bars as another process's platform chrome.
/// </summary>
/// <remarks>
///     Here rather than in either end because neither end can see the other: the head is in Rask.Native, the
///     app is hosted by Rask.Server or Rask.Wasm, and none of those three reference each other. Rask.Chrome
///     is the assembly all of them already have — the same reason the descriptor itself lives here. Two
///     copies of a string like this drift silently and fail as "the bars just do not appear".
/// </remarks>
internal static class NativeShellProtocol
{
    /// <summary>
    ///     The request header a native head sets on the page load it initiates, telling the server that this
    ///     document will be displayed inside native chrome.
    /// </summary>
    public const string ShellHeader = "X-Rask-Shell";

    /// <summary>The one <see cref="ShellHeader" /> value that means anything today.</summary>
    public const string NativeShell = "native";
}
