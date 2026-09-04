namespace Rask.Example.Shared.Features;

public sealed partial class PropsCommandDemo : Component
{
    // command/commandfor generalise what popovertarget does for popovers: the button names the element
    // it acts on and the action to invoke, and the browser does the rest. There is no OnClick here and
    // no JavaScript anywhere — opening and closing the dialog is entirely declarative.
    //
    // `show-modal` and `close` are two of the built-in actions; a custom `--name` would dispatch a
    // CommandEvent instead of invoking one.
    protected override Component? Render() =>
        Div[
            Button
                .Class(Tw.BtnOutlinePrimary)
                .Command("show-modal")
                .CommandFor("props-command-dialog")["Open the dialog"],
            Dialog.Id("props-command-dialog").Class("p-3 border-0 rounded shadow")[
                P.Class("mb-3")["Opened by ", Code["command"], ", with no handler on either side."],
                Button
                    .Class(Tw.BtnSecondary)
                    .Command("close")
                    .CommandFor("props-command-dialog")["Close"]]];
}
