namespace Rask;

/// <summary>
///     A kit component that draws the button a dropdown opens from. A dropdown decorates an ELEMENT it is given
///     as its trigger; one of these is not an element, so it is told what it opens and wires the button itself.
/// </summary>
internal interface IUiTrigger
{
    /// <summary>Makes the component's button the one that opens <paramref name="panelId" />. A chain step of the kit's own.</summary>
    Component Invoking(string panelId, bool open);
}
