namespace Rask.Ui;

/// <summary>
/// Applies the reader's theme before the first paint, remembers it, and follows the operating system
/// when they have not chosen one.
/// </summary>
/// <remarks>
/// <para>
/// The other half of <see cref="UiThemePicker" />. The picker reports a choice and daisyUI's CSS paints
/// it, with no script — but nothing in CSS can REMEMBER, so a navigation or a full-document morph
/// dropped the choice and the palette snapped back. Put this in the root component's head assets and the
/// pair is complete:
/// </para>
/// <code>
/// protected override Component? HeadAssets => [Title["…"], UiThemeScript];
/// </code>
/// <para>
/// WITH NO SAVED CHOICE IT WRITES NO <c>data-theme</c> AT ALL, and that absence is the feature rather
/// than an omission. daisyUI compiles the default palette as <c>:where([data-rask-ui])</c> for light and
/// <c>[data-rask-ui]:not([data-theme])</c> inside <c>@media (prefers-color-scheme: dark)</c> for dark, so
/// a theme scope carrying no <c>data-theme</c> already follows the reader's machine — in CSS, with
/// nothing running, and re-painting the moment they flip their OS between light and dark while the page
/// sits open. A script that read <c>matchMedia</c> once and stamped a name could not do that last part.
/// </para>
/// <para>
/// It runs where it is rendered, which must be BEFORE the stylesheets in <c>&lt;head&gt;</c>: this is the
/// only code on the page that runs ahead of the first paint, so it is the only place a saved dark theme
/// can be applied without a flash of light. It also re-applies after every morph, through Rask's
/// <c>raskAfterMorph</c> hook — a full-document morph strips attributes off <c>&lt;html&gt;</c>, so the
/// attribute has to be put back rather than rendered once.
/// </para>
/// <para>
/// NO C# EVENT HANDLERS, deliberately, and this is the constraint the whole design is shaped by. Handler
/// ids are handed out in render order, so putting one handler into the chrome of every page shifts every
/// id after it — and an island (Vue, Lit, React) captures its callback id from the prerendered markup, so
/// the ids moving underneath it breaks its clicks SILENTLY, on a page that still looks alive. Measured on
/// the showcase: an island's pre-boot callback id went h28 -> h63 with thirty-five theme buttons rendered
/// and h28 -> h29 with one. One is already too many. Owning the value from JavaScript costs zero handler
/// slots.
/// </para>
/// <para>
/// The stored value is checked against the themes that EXIST rather than against a shape.
/// <c>localStorage</c> is reader-writable and <c>data-theme</c> is matched by value, so
/// <c>data-theme="dracola"</c> passes any plausible regular expression, matches no block daisyUI
/// compiled, and leaves every <c>--color-base-*</c> undefined on the element the document inherits from
/// — a page that renders fully laid out with no colour in it, reporting nothing. An unknown value means
/// "no choice", which falls back to the operating system.
/// </para>
/// </remarks>
public sealed partial class UiThemeScript : Component
{
    /// <summary>
    /// The <c>localStorage</c> key the choice is kept under. Defaults to <c>rask-theme</c>.
    /// </summary>
    /// <remarks>
    /// Worth setting only when two Rask apps share an origin and should not share a palette. It is
    /// embedded in a JavaScript string literal, so it is restricted to the characters a key needs.
    /// </remarks>
    public string StorageKey { get; set; } = "rask-theme";

    /// <summary>The themes the reader may have stored. Defaults to every theme the kit ships.</summary>
    /// <remarks>
    /// Narrow it to match a narrowed <see cref="UiThemePicker.Themes" />: a value outside this list is
    /// treated as no choice, so a theme removed from the picker also stops being restorable by anyone who
    /// had already chosen it.
    /// </remarks>
    public IReadOnlyList<UiThemeName>? Themes { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Script[Raw.Value(Js(StorageKey, Themes ?? UiTheme.All))];

    /// <summary>
    /// The snippet, built around the key and the list of themes that exist.
    /// </summary>
    /// <remarks>
    /// Written out of <see cref="UiTheme.All" /> rather than spelled as a literal, so the allowlist cannot
    /// drift from the stylesheet: a theme added to the kit is restorable the day it ships, and one removed
    /// stops being stamped.
    /// </remarks>
    internal static string Js(string storageKey, IReadOnlyList<UiThemeName> themes)
    {
        var key = Escape(storageKey);
        var names = string.Join(' ', themes.Where(t => t != UiThemeName.System).Select(UiTheme.Value));

        return
            "(function(){var d=document.documentElement,K='" + key + "',S='" + UiTheme.SystemValue + "'," +
            "T='" + names + "'.split(' ');" +
            "function read(){try{var v=localStorage.getItem(K);" +
            "return v&&T.indexOf(v)>=0?v:null;}catch(e){return null;}}" +
            "function apply(t){if(t)d.setAttribute('data-theme',t);else d.removeAttribute('data-theme');}" +
            "function mark(){var t=read()||S,i=document.querySelectorAll('input.theme-controller');" +
            "for(var n=0;n<i.length;n++)i[n].checked=i[n].value===t;}" +
            "apply(read());" +
            "window.raskTheme=function(){return read()||S;};" +
            "window.raskSetTheme=function(t){if(t&&T.indexOf(t)>=0){try{localStorage.setItem(K,t);}" +
            "catch(e){}}else{t=null;try{localStorage.removeItem(K);}catch(e){}}" +
            "apply(t);mark();return t||S;};" +
            "document.addEventListener('change',function(e){var t=e.target;" +
            "if(t&&t.classList&&t.classList.contains('theme-controller'))window.raskSetTheme(t.value);});" +
            "if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',mark);" +
            "else mark();" +
            "var prev=window.raskAfterMorph;" +
            "window.raskAfterMorph=function(){apply(read());mark();" +
            "if(typeof prev==='function')prev();};})();";
    }

    /// <summary>
    /// Keeps <see cref="StorageKey" /> inside the JavaScript string literal it is written into.
    /// </summary>
    /// <remarks>
    /// This snippet is emitted through <c>Raw</c>, which is verbatim by definition — so a key carrying a
    /// quote would not produce a broken string, it would produce executable script in the head of every
    /// page. An allowlist rather than escaping: a storage key needs none of the characters that would make
    /// this interesting, and rejecting is a clearer contract than encoding.
    /// </remarks>
    private static string Escape(string key)
    {
        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':'))
            {
                throw new ArgumentException(
                    $"'{key}' is not usable as a storage key: it is written into a script in the document "
                    + "head, so it is restricted to letters, digits and - _ . :",
                    nameof(key));
            }
        }

        return key;
    }
}
