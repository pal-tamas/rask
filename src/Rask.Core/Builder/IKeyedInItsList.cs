namespace Rask.Core;

/// <summary>
///     A component whose <c>Key</c> names it among the rows of the component it is handed to, not among everything
///     the component that writes it renders: a select's option, which the select draws as a row of its own list.
/// </summary>
/// <remarks>
///     <para>
///     A component's key is otherwise its identity among everything its OWNER writes (#685, #1215), so two selects on
///     one page whose options are keyed by the same ids were one set of keys: the page reported the repeat, and the
///     instance one option held moved to the other as rows came and went. Both apps worked round it by prefixing
///     every key with the bound member (#1284).
///     </para>
///     <para>
///     A component that holds nothing of its own — every prop is written by its chain at every render, as an
///     element's are — gains nothing from keeping an instance, so it keeps positional identity as an element does,
///     and the key is left to the component that draws it: the select keys its row by it, inside its own list.
///     </para>
/// </remarks>
internal interface IKeyedInItsList;
