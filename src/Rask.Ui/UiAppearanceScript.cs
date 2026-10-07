namespace Rask;

/// <summary>
/// Dark mode, the way Flux UI does it: puts a <c>dark</c> class on <c>&lt;html&gt;</c> before the first
/// paint, from the reader's stored appearance — <c>light</c>, <c>dark</c> or <c>system</c>.
/// </summary>
/// <remarks>
/// <para>
/// Put it in the root component's head assets, BEFORE the stylesheets — it is the only code that runs
/// ahead of the first paint, so it is the only place a dark page can start dark:
/// </para>
/// <code>
/// protected override Component? HeadAssets => [Title["…"], Ui.AppearanceScript, /* stylesheets */];
/// </code>
/// <para>
/// A control needs two properties, the ones Flux documents as <c>Flux.appearance</c> and
/// <c>Flux.dark</c>: <c>Rask.appearance</c> gets or sets <c>'light' | 'dark' | 'system'</c>, and
/// <c>Rask.dark</c> gets or sets whether the page is dark right now. A toggle is
/// <c>Rask.dark = !Rask.dark</c> — no C# handler, so it costs no handler id and no round trip.
/// </para>
/// <para>
/// <c>system</c> is the default and is stored as NO key at all; it follows
/// <c>prefers-color-scheme</c> while the page is open. Only the exact strings <c>light</c> and
/// <c>dark</c> are a choice: <c>localStorage</c> is reader-writable, so anything else means system.
/// The class is put back after every morph (<c>raskAfterMorph</c>), because a full-document morph
/// rewrites <c>&lt;html&gt;</c>'s attributes, and another tab's change is followed through the
/// <c>storage</c> event.
/// </para>
/// </remarks>
public sealed partial class UiAppearanceScript : Component
{
    /// <summary>
    /// The <c>localStorage</c> key the appearance is kept under. Defaults to <c>rask.appearance</c>.
    /// </summary>
    /// <remarks>
    /// Worth setting only when two Rask apps share an origin and should not share an appearance.
    /// Restricted to letters, digits and <c>- _ . :</c>, because it is written into a script.
    /// </remarks>
    public string StorageKey { get; set; } = "rask.appearance";

    /// <inheritdoc />
    protected override Component? Render() =>
        Script[Raw.Value(Js(StorageKey))];

    // GOES WITH daisyUI: the components it still draws read data-theme, so the scheme is mirrored there
    // as daisyUI's `dark` or `light`. Delete this constant and its one use with the plugin.
    private const string DaisyBridge = "d.setAttribute('data-theme',on?'dark':'light');";

    internal static string Js(string storageKey) =>
        "(function(){var d=document.documentElement,K='" + Checked(storageKey) + "'," +
        "M=window.matchMedia('(prefers-color-scheme: dark)'),A=read();" +
        "function ok(v){return v==='light'||v==='dark';}" +
        "function read(){try{var v=localStorage.getItem(K);return ok(v)?v:'system';}" +
        "catch(e){return 'system';}}" +
        "function dark(){return A==='dark'||(A==='system'&&M.matches);}" +
        "function apply(){var on=dark();d.classList.toggle('dark',on);" + DaisyBridge + "}" +
        "function set(v){A=ok(v)?v:'system';" +
        "try{if(A==='system')localStorage.removeItem(K);else localStorage.setItem(K,A);}catch(e){}" +
        "apply();}" +
        "apply();" +
        "var R=window.Rask=window.Rask||{};" +
        "Object.defineProperty(R,'appearance',{configurable:true,get:function(){return A;},set:set});" +
        "Object.defineProperty(R,'dark',{configurable:true,get:dark," +
        "set:function(on){set(on?'dark':'light');}});" +
        "M.addEventListener('change',apply);" +
        "window.addEventListener('storage',function(e){if(e.key===K||e.key===null){A=read();apply();}});" +
        "var prev=window.raskAfterMorph;" +
        "window.raskAfterMorph=function(){apply();if(typeof prev==='function')prev();};})();";

    // The script is emitted through Raw, which is verbatim: a key carrying a quote would be executable
    // script in the head of every page. An allowlist, because a storage key needs none of those characters.
    private static string Checked(string key)
    {
        if (key.Length == 0 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':')))
        {
            throw new ArgumentException(
                $"'{key}' is not usable as a storage key: it is written into a script in the document "
                + "head, so it is restricted to letters, digits and - _ . :",
                nameof(key));
        }

        return key;
    }
}
