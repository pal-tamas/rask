using Rask.Core;
using Rask.Web;

namespace Rask.Printing;

/// <summary>The chain step that turns a button into a print button.</summary>
public static class PrintSteps
{
    /// <summary>A click on this button opens the browser's print dialog: <c>Button.Print()["Print this page"]</c>.</summary>
    public static HTMLButtonElement Print(this HTMLButtonElement button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return button.OnClick(() => Window.Print());
    }
}
