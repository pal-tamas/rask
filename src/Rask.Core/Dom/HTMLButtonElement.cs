using Rask.Core.Forms;

namespace Rask.Core;

// What MDN cannot know of a <button>: drawn disabled, the browser sends it no event, so every handler it registers
// then is refused (#1273, IFormControlElement).
public sealed partial class HTMLButtonElement : IFormControlElement
{
    string IFormControlElement.Control => "button";

    bool IFormControlElement.DrawnDisabled => DrawnDisabled.Is(Disabled, this);

    string? IFormControlElement.PostsUnder => Name;
}
