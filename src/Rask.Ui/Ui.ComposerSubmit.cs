namespace Rask;

public static partial class Ui
{
    /// <summary>Which key sends a <see cref="UiComposer{T}" />'s form.</summary>
    public enum ComposerSubmit
    {
        /// <summary>Ctrl+Enter or Cmd+Enter; Enter alone starts a new line. Flux's <c>cmd+enter</c>.</summary>
        CmdEnter = 0,

        /// <summary>Enter; Shift+Enter starts a new line.</summary>
        Enter,
    }
}
