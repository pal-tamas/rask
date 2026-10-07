namespace Rask;

public static partial class Ui
{
    /// <summary>What opens a <see cref="UiTimePicker{T}" />.</summary>
    public enum TimePickerType
    {
        /// <summary>A button showing the chosen time, Flux's default.</summary>
        Button = 0,

        /// <summary>Hour, minute and AM/PM fields to type into, with the list beside them.</summary>
        Input,
    }
}
