namespace Rask.Core.Forms;

// When a bound field writes what was typed back to the model. Unset, it waits for the next action — a press on
// a button, a submit, Enter — as Livewire's `wire:model` does. `Live` types through after a short pause,
// `Debounce` names the pause, `Blur` waits for the field to be left.
//
// One int, so the three steps cost a control one field: a bit for Live, a bit for Blur, and the pause in
// milliseconds plus one beneath them. Each step keeps what it was given and nothing else, so a step the next
// render leaves out — which arrives as null — takes only its own answer away, in whatever order they arrive.
// Blur and Debounce are two answers to one question: naming either takes the other back.
//
// A form over an operation (Form.For) asks a field that names none of them to commit as it is left, so the domain's
// dry run answers then (Under). `Defer` is the field saying it waits for the next action all the same; any other
// step it is named with wins over it.
internal struct BindTiming
{
    private const int LiveBit = 1 << 30;
    private const int BlurBit = 1 << 29;
    private const int DeferBit = 1 << 28;
    private const int PauseMask = DeferBit - 1;

    // Livewire's own pause for `wire:model.live`.
    private const int LivePause = 150;

    private int _steps;

    private readonly bool HasPause => (_steps & PauseMask) != 0;

    private readonly bool OnBlur => !HasPause && (_steps & BlurBit) != 0;

    // The steps that say when the field is sent; Defer only keeps a form's default away.
    private readonly int Timed => _steps & ~DeferBit;

    /// <summary>Whether the field says nothing until the next action. The default.</summary>
    public readonly bool WaitsForAction => Timed == 0;

    /// <summary>Whether the field is sent at every keystroke, with no pause.</summary>
    public readonly bool AtEveryKey => (_steps & PauseMask) == 1;

    /// <summary>The pause in milliseconds, or 0 when the field counts none.</summary>
    public readonly int Pause
    {
        get
        {
            if (HasPause)
            {
                return (_steps & PauseMask) - 1;
            }

            return Timed == LiveBit ? LivePause : 0;
        }
    }

    public bool? Live
    {
        readonly get => (_steps & LiveBit) != 0 ? true : null;
        set => _steps = value == true ? _steps | LiveBit : _steps & ~LiveBit;
    }

    public bool? Blur
    {
        readonly get => OnBlur ? true : null;
        set => _steps = value == true ? (_steps | BlurBit) & ~PauseMask : _steps & ~BlurBit;
    }

    public TimeSpan? Debounce
    {
        readonly get => HasPause ? TimeSpan.FromMilliseconds((_steps & PauseMask) - 1) : null;
        set
        {
            if (value is not { } pause)
            {
                _steps &= ~PauseMask;
                return;
            }

            var milliseconds = pause > TimeSpan.Zero ? (int)Math.Clamp(Math.Ceiling(pause.TotalMilliseconds), 1, PauseMask - 1) : 0;
            _steps = (_steps & (LiveBit | DeferBit)) | (milliseconds + 1);
        }
    }

    public bool? Defer
    {
        readonly get => (_steps & DeferBit) != 0 ? true : null;
        set => _steps = value == true ? _steps | DeferBit : _steps & ~DeferBit;
    }

    /// <summary>
    ///     The timing the field is drawn with in <paramref name="form" />: one that names no step commits as it is left
    ///     where the form asks for that (a form over an operation), and waits for the next action everywhere else.
    /// </summary>
    public readonly BindTiming Under(EditContext? form) =>
        _steps == 0 && form?.CommitsOnLeave == true ? new BindTiming { Blur = true } : this;
}
