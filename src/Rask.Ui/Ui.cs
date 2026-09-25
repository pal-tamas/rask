[assembly: RaskChainGroup(typeof(Rask.Ui))]

namespace Rask;

/// <summary>
///     The Rask UI kit: every component, reached as <c>Ui.Button</c>, <c>Ui.Card</c>, <c>Ui.DataGrid</c>, and every
///     option it takes, as <c>Ui.Tone</c>, <c>Ui.Size</c>, <c>Ui.Variant</c>.
/// </summary>
/// <remarks>
///     <para>
///         Typing <c>Ui.</c> lists the whole kit. A component's entry is a member of this class rather than a bare
///         name, so the kit never shadows an HTML tag: <c>Ui.Button</c> is the kit's button, <c>Button</c> is the
///         <c>&lt;button&gt;</c>. The component TYPES keep their <c>Ui</c> prefix — <c>UiButton</c> — which is the name
///         a signature, a hover or a compiler message uses.
///     </para>
///     <code>
///     Ui.Card[
///         Ui.Button.Tone(Ui.Tone.Primary).OnClick(Save)["Save"],
///         Ui.Badge.Variant(Ui.Variant.Soft)["new"]
///     ]
///     </code>
///     <para>
///         In the <c>Rask</c> namespace, so the <c>using Rask;</c> every template carries is all it needs — and so a
///         bare <c>Ui</c> means this class from inside any <c>Rask.*</c> namespace too.
///     </para>
/// </remarks>
public static partial class Ui;
