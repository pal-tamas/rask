namespace Rask.Core.Forms;

// When a bound field writes what was typed back to the model. Unset, it waits for the next action — a press on
// a button, a submit, Enter — as Livewire's `wire:model` does. `Live` types through after a short pause,
// `Debounce` names the pause, `Blur` waits for the field to be left.
//
// One int, so the three steps cost a control one field: a bit for Live, a bit for Blur, and the pause in
// milliseconds plus one beneath them. Each step keeps what it was given and nothing else, so a step the next
// render leaves out — which arrives as null — takes only its own answer away, in whatever order they arrive.
// Blur and Debounce are two answers to one question: naming either takes the other back.
internal struct BindTiming
{
    private const int LiveBit = 1 << 30;
    private const int BlurBit = 1 << 29;
    private const int PauseMask = BlurBit - 1;

    // Livewire's own pause for `wire:model.live`.
    private const int LivePause = 150;

    private int _steps;

    private readonly bool HasPause => (_steps & PauseMask) != 0;

    private readonly bool OnBlur => !HasPause && (_steps & BlurBit) != 0;

    /// <summary>Whether the field says nothing until the next action. The default.</summary>
    public readonly bool WaitsForAction => _steps == 0;

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

            return _steps == LiveBit ? LivePause : 0;
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
            _steps = (_steps & LiveBit) | (milliseconds + 1);
        }
    }
}
