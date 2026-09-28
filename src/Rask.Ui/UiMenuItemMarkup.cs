namespace Rask;

/// <summary>What makes a row a menu item inside a dropdown, for every kind of item.</summary>
internal static class UiMenuItemMarkup
{
    /// <summary>
    ///     Makes <paramref name="element" /> the menu item at <paramref name="ordinal" />: its id, role, tab stop,
    ///     ARIA, and the <c>data-*</c> hooks for the cursor, a pick that keeps the menu open, and a checked row.
    /// </summary>
    internal static T AsMenuItem<T>(
        T element,
        UiMenuLevel? level,
        int ordinal,
        string role,
        Dictionary<string, string?> aria,
        bool keepOpen,
        bool isChecked)
        where T : Element
    {
        var data = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (isChecked)
        {
            data["checked"] = "";
        }

        // Chain steps, not property writes: a chain-built element renders what its chain was given, and a later
        // assignment to the property is invisible to it (RASK045 catches the non-generic case).
        if (level is null)
        {
            element = element.Role(role).Aria(aria);
            return data.Count > 0 ? element.Data(data) : element;
        }

        var scope = level.Scope;
        element = element.Id(scope.ItemId(ordinal)).Role(role).TabIndex(-1).Aria(aria);

        if (ordinal == scope.Active)
        {
            // Flux's styling hook for the row under the cursor, named for what it is.
            data["highlighted"] = "";
        }

        if (keepOpen)
        {
            data["rask-keep-open"] = "";
        }

        return data.Count > 0 ? element.Data(data) : element;
    }
}
