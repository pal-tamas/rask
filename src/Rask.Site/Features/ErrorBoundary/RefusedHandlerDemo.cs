namespace Rask.Site.Features;

// The app saying no on purpose: a RefusedException's message is written for the reader, so it is the one
// that is shown — as the error toast, in place of "That did not work". Nothing to catch, no boundary of
// yours, no reference to quote: it is not a fault, and the page keeps what the handler did before it threw.
public sealed partial class RefusedHandlerDemo : Component
{
    private bool _closed;

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-3").Id("refused-host")[
            Ui.Button.Primary.Id("refused-close").OnClick(Close)["Close the invoice"],
            Ui.Badge.Color(_closed ? Ui.Color.Zinc : Ui.Color.Green).Id("refused-state")[_closed ? "Closed" : "Open"]
        ];

    private void Close()
    {
        if (_closed)
        {
            throw new RefusedException("The invoice is already closed.");
        }

        _closed = true;
    }
}
