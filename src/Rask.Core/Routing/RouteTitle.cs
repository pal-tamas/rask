using Rask.Core.Live;

namespace Rask.Core.Routing;

/// <summary>The title the route's pages declare, and the components that show it.</summary>
internal sealed class RouteTitle
{
    // Each component that read the title while rendering, and whether it did so ahead of the router —
    // the one place a reader can have shown the title of the page before this one.
    private readonly Dictionary<Component, bool> _readers = new(ReferenceEqualityComparer.Instance);
    private string? _value;

    public string? Read()
    {
        if (LiveRenderContext.CurrentSync is { } ctx)
        {
            _readers[ctx.WalkParent] = ctx.Route is null;
        }

        return _value;
    }

    /// <summary>Takes the title the router just read, and re-renders every reader when it differs.</summary>
    /// <returns>Whether a reader that already rendered in this walk now shows the old title.</returns>
    public bool Publish(string? value)
    {
        if (string.Equals(_value, value, StringComparison.Ordinal))
        {
            return false;
        }

        _value = value;
        var stale = false;
        foreach (var (reader, ahead) in _readers)
        {
            if (reader.IsTornDown)
            {
                _readers.Remove(reader);
                continue;
            }

            reader.MarkDirtyForFrame();
            stale |= ahead;
        }

        return stale;
    }
}
