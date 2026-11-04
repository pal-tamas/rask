namespace Rask.Core.Forms;

/// <summary>
///     An element the browser sends no event to while it is drawn disabled: a button, an input, a select, a
///     textarea — itself, or inside a <c>&lt;fieldset disabled&gt;</c>. Every handler it registers then is refused
///     (<see cref="DisabledHandler" />, #1273), whatever it is for.
/// </summary>
internal interface IFormControlElement
{
    /// <summary>What the control is called in the log: <c>button</c>, <c>input</c>, <c>select</c>, <c>textarea</c>.</summary>
    string Control { get; }

    /// <summary>Whether the control is drawn disabled itself: its <c>Disabled</c>, or a <c>disabled</c> attribute.</summary>
    bool DrawnDisabled { get; }

    /// <summary>The name it posts under, said in the log; read only when it refuses.</summary>
    string? PostsUnder { get; }
}
