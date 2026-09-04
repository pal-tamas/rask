using Rask.Core;

namespace Rask.Ui.Tests;

/// <summary>
///     A markup host, because the chain's entry for a component only exists inside one — a bare
///     <c>UiIcon</c> in a plain test class is the type, not the chain entry.
/// </summary>
internal sealed partial class Host : Component
{
    public required UiIconName IconName { get; set; }

    public string? IconClass { get; set; }

    protected override Component? Render() => UiIcon.Name(IconName).Class(IconClass);
}
