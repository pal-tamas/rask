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
///     state of its own. It also records a press on the collapse control made before the runtime's hook has
///     loaded, under the same key and in the same words, so that press is kept too.
///     </para>
/// </remarks>
public sealed partial class UiSidebarScript : Component
{
    // Two halves, each written once.
    //
    // RECORD: the runtime's hook is what stores a change, and in a WebAssembly app it arrives after the page
    // can already be pressed. A press in that gap would change the box and store nothing — and the hook,
    // arriving, would put the box back to what the last visit left. So this listens too, from the first
    // byte, and writes the same key the same way; when the hook is there both write the same value.
    //
    // RESTORE: the checkbox does not exist while the head is parsed, so the script waits for the parser to
    // write it — an observer's callback runs before the browser paints what was just parsed.
    internal const string Js =
        "(function(){var s='[data-rask-persist=\"" + UiSidebarState.RailKey + "\"]';"
        + "document.addEventListener('change',function(e){var b=e.target;if(!b||!b.matches||!b.matches(s))return;"
        + "try{localStorage.setItem('" + UiSidebarState.RailKey + "',b.checked?'true':'false');}catch(x){}},true);"
        + "try{if(localStorage.getItem('" + UiSidebarState.RailKey + "')!=='true')return;}catch(e){return;}"
        + "function set(){var b=document.querySelector(s);if(b)b.checked=true;return !!b;}"
        + "if(set())return;"
        + "var o=new MutationObserver(function(){if(set())o.disconnect();});"
        + "o.observe(document.documentElement,{childList:true,subtree:true});"
        + "document.addEventListener('DOMContentLoaded',function(){o.disconnect();});})();";

    /// <inheritdoc />
    protected override Component? Render() => Script[Raw.Value(Js)];
}
