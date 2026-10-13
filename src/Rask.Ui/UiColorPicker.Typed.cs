using Rask.Core;

namespace Rask;

// A colour typed into the trigger or into the popover: taken on Enter or on leaving the field, as on Flux.
public sealed partial class UiColorPicker
{
    // What is being typed, and into which input, since that input last showed the colour.
    private string? _typing;

    private ElementRef<HTMLInputElement>? _typingIn;

    // The input Enter was pressed in, to be left once its change has been taken.
    private ElementRef<HTMLInputElement>? _leaves;

    // Text that was no colour, and the input it stays in until a colour is typed there.
    private (string Text, ElementRef<HTMLInputElement> Input)? _refused;

    private void Typing(string text, ElementRef<HTMLInputElement> input) => (_typing, _typingIn) = (text, input);

    // Enter takes what was typed and leaves the input. The browser reports an edited input's change on Enter by
    // itself, and the input is left once that change is taken: left from here, the change would be reported
    // inside this handler's own call, where the runtime holds it back until the next event.
    private void TypingKey(KeyboardEvent e, ElementRef<HTMLInputElement> input, bool opens)
    {
        if (!string.Equals(e.Key, Keys.Enter, StringComparison.Ordinal))
        {
            if (opens)
            {
                OpeningKey(e);
            }

            return;
        }

        var edited = ReferenceEquals(_typingIn, input) && !string.Equals(_typing, Said(input), StringComparison.Ordinal);
        if (edited)
        {
            _leaves = input;
        }
        else
        {
            UiReach.Send(() => input.Blur());
        }
    }

    // A colour is taken and written in the picker's format; nothing clears. Anything else Flux puts back; a
    // picker bound in a form refuses it instead, and it stays where the reader can see it (RefuseAsync).
    private async Task TypedAsync(string text, ElementRef<HTMLInputElement> input)
    {
        var inPopover = ReferenceEquals(input, _popoverInput);
        if (string.IsNullOrWhiteSpace(text) || UiColorValue.TryParse(text, out _))
        {
            _refused = null;
            var pick = UiColorValue.TryParse(text, out var color) ? new Pick(color, Empty: false) : Pick.None;
            await (inPopover ? ChooseAsync(pick) : TakeAsync(pick)).ConfigureAwait(false);
        }
        else if (Refuses)
        {
            _refused = (text, input);
            await RefuseAsync().ConfigureAwait(false);
        }

        var said = Said(input);
        UiReach.Send(() => input.SetRangeText(said, 0, int.MaxValue));
        (_typing, _typingIn, _roving) = (null, null, null);
        if (ReferenceEquals(_leaves, input))
        {
            _leaves = null;
            UiReach.Send(() => input.Blur());
        }
    }

    // What an input says: the text it refused, or the colour — the page's on the trigger, the popover's inside it.
    private string Said(ElementRef<HTMLInputElement> input)
    {
        if (_refused is var (text, refusedIn) && ReferenceEquals(refusedIn, input))
        {
            return text;
        }

        return (ReferenceEquals(input, _popoverInput) ? _shown : _kept).Text(Written);
    }

    private Task ClearAsync()
    {
        _refused = null;

        return TakeAsync(Pick.None);
    }
}
