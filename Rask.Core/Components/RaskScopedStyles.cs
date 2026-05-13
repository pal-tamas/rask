using Rask.Core.ScopedCss;

namespace Rask.Core.Components;

public sealed class RaskScopedStyles : Component
{
    private static readonly IReadOnlyDictionary<string, string?> _marker =
        new Dictionary<string, string?> { ["rask-scoped"] = "" };

    protected override Component Render()
    {
        var hash = ScopedCssRegistry.CurrentHash;
        if (hash is null)
        {
            return new Raw(string.Empty);
        }

        return new Link
        {
            Rel = "stylesheet",
            Href = $"/_rask/scoped.css?v={hash}",
            Data = _marker
        };
    }
}
