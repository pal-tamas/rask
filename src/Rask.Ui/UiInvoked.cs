namespace Rask;

/// <summary>What an <see cref="IUiTrigger" /> was told: the panel it opens, and whether that panel is open now.</summary>
internal readonly record struct UiInvoked(string PanelId, bool Open)
{
    /// <summary>The button, as the invoker of the panel.</summary>
    internal T On<T>(T button)
        where T : Element => UiInvoker.Decorate(button, PanelId, "true", Open);
}
