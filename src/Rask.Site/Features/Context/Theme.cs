using Rask.Core.Components;

namespace Rask.Site.Features;

// A value provided high in the tree and read deep in the tree, with an intermediate component
// (ThemeCard) that knows nothing about the theme — no prop drilling. Toggling re-renders the
// provider's owner; the deep consumer (ThemeBadge) bypasses the render cache and picks up the
// new value even though the intermediate ThemeCard stays cached between the two.

public sealed record Theme(string Name, bool IsDark)
{
    public static readonly Theme Light = new("Light", false);
    public static readonly Theme Dark = new("Dark", true);
}
