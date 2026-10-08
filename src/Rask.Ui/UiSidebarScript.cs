namespace Rask;

/// <summary>
///     Restores a sidebar's collapsed rail before first paint. For the head of a WebAssembly app whose
///     <see cref="UiSidebar" /> is <see cref="Ui.SidebarCollapsible.Always" />.
/// </summary>
/// <remarks>
///     <para>
///     The rail is a checkbox the runtime keeps in <c>localStorage</c> (<c>data-rask-persist</c>, under the key
///     Flux's own script uses). On a Server page the runtime restores it before the first paint; a WebAssembly
///     app's runtime loads after the prerendered page is on screen, so a reader who left the sidebar narrow
///     would watch it open wide and snap shut. This checks the box the moment the parser reaches it.
///     </para>
///     <para>
///     Add it to <c>HeadAssets</c>, beside <see cref="UiAppearanceScript" />. It takes no handler and keeps no
///     state of its own: the runtime's hook stays the one writer.
///     </para>
/// </remarks>
public sealed partial class UiSidebarScript : Component
{
    // The checkbox does not exist while the head is parsed, so the script waits for the parser to write it —
    // an observer's callback runs before the browser paints what was just parsed.
    internal const string Js =
        "(function(){try{if(localStorage.getItem('" + UiSidebarState.RailKey + "')!=='true')return;}catch(e){return;}"
        + "var s='[data-rask-persist=\"" + UiSidebarState.RailKey + "\"]';"
        + "function set(){var b=document.querySelector(s);if(b)b.checked=true;return !!b;}"
        + "if(set())return;"
        + "var o=new MutationObserver(function(){if(set())o.disconnect();});"
        + "o.observe(document.documentElement,{childList:true,subtree:true});"
        + "document.addEventListener('DOMContentLoaded',function(){o.disconnect();});})();";

    /// <inheritdoc />
    protected override Component? Render() => Script[Raw.Value(Js)];
}
