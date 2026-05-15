using System.Linq.Expressions;
using Rask.Core.DataGrids;
using Rask.Core.Live;

namespace Rask.Core.Components;

// Sort-toggle button for one column of the ambient DataGridContext<TRow>. The SortBy
// expression doubles as the runtime selector (compiled once) and the source of the sort
// key (auto-derived from the property name; override via Key when sorting on a computed
// expression). Shift-click adds the column as a secondary sort; plain click cycles
// none → asc → desc → none on a single rule set.
public sealed class DataGridSortButton<TRow> : Component
{
    public required Expression<Func<TRow, object?>> SortBy { get; set; }
    public string? Key { get; set; }

    private Expression? _cachedExpression;
    private Func<TRow, object?>? _cachedSelector;
    private string? _cachedKey;

    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        var (selector, key) = ResolveCache();
        var ctx = DataGridScope.CurrentAs<TRow>();
        var passthrough = Children ?? Array.Empty<Child>();
        if (ctx is null)
        {
            return Components.Button(Type: "button")[passthrough];
        }

        Action<MouseModifiers> onClick = mods => ctx.ToggleSort(key, selector, mods.Shift);
        var live = LiveRenderContext.Current;
        if (live is null)
        {
            return Components.Button(Type: "button")[passthrough];
        }

        var handlerId = live.RegisterHandler(onClick);
        var data = new Dictionary<string, string?> { ["rask-on-click"] = handlerId };
        return Components.Button(Type: "button", Data: data)[passthrough];
    }

    private (Func<TRow, object?> Selector, string Key) ResolveCache()
    {
        if (!ReferenceEquals(_cachedExpression, SortBy) || _cachedSelector is null || _cachedKey is null)
        {
            _cachedExpression = SortBy;
            _cachedSelector = SortBy.Compile();
            _cachedKey = Key ?? DataGridKeyExtractor.Extract(SortBy);
        }
        else if (Key is { } explicitKey && _cachedKey != explicitKey)
        {
            _cachedKey = explicitKey;
        }

        return (_cachedSelector!, _cachedKey!);
    }
}
