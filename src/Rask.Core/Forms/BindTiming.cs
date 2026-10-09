namespace Rask.Core.Forms;

// When a bound field writes what was typed back to the model: on every keystroke, on leaving the field
// (Blur), or once typing has paused (Debounce). One int — 0 live, -1 blur, otherwise the pause in
// milliseconds — so the two steps cost a control one field, and the later of the two is the one that holds.
internal struct BindTiming
{
    private const int OnBlur = -1;

    private int _wait;

    /// <summary>Whether the field waits at all. A live field does not.</summary>
    public readonly bool Waits => _wait != 0;

    /// <summary>The pause in milliseconds, or 0 when the field is live or waits for blur.</summary>
    public readonly int Pause => _wait > 0 ? _wait : 0;

    public bool? Blur
    {
        readonly get => _wait == OnBlur ? true : null;
        set
        {
            if (value == true)
            {
                _wait = OnBlur;
            }
            else if (_wait == OnBlur)
            {
                _wait = 0;
            }
        }
    }

    public TimeSpan? Debounce
    {
        readonly get => _wait > 0 ? TimeSpan.FromMilliseconds(_wait) : null;
        set
        {
            if (value is { } pause && pause > TimeSpan.Zero)
            {
                _wait = (int)Math.Clamp(Math.Ceiling(pause.TotalMilliseconds), 1, int.MaxValue);
            }
            else if (_wait > 0)
            {
                _wait = 0;
            }
        }
    }
}
