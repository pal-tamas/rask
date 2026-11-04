using Rask.Core.Forms;

namespace Rask.Blazor;

/// <summary>
///     A button, input, select or textarea in a hosted component's render frames, as the browser will draw it
///     (#1274): what it is, the name it posts under, and whether it is drawn disabled — by its own <c>disabled</c>
///     attribute, or inside a hosted <c>&lt;fieldset disabled&gt;</c> outside its first legend.
/// </summary>
/// <remarks>
///     The control a refused handler names (<see cref="DisabledHandler" />): a browser sends no event to a disabled
///     control, so whatever arrives for one was written by hand, and it is refused as Rask refuses its own.
/// </remarks>
/// <param name="Tag">The element's tag: <c>button</c>, <c>input</c>, <c>select</c>, <c>textarea</c>.</param>
/// <param name="Name">Its <c>name</c> attribute, or null.</param>
/// <param name="Disabled">Whether its own frames or a hosted fieldset draw it disabled.</param>
internal readonly record struct BlazorControl(string Tag, string? Name, bool Disabled) : IFormControlElement
{
    string IFormControlElement.Control => Tag;

    bool IFormControlElement.DrawnDisabled => Disabled;

    string? IFormControlElement.PostsUnder => Name;
}
