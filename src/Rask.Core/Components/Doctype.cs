namespace Rask.Core.Components;

/// <summary>
///     Emits exactly <c>&lt;!DOCTYPE html&gt;</c> — no attributes, no children, no wrapper. A page's root
///     render must emit the whole shell, so this is normally the first item of <c>[Doctype(),
///     Html(...)]</c>. <see href="https://developer.mozilla.org/en-US/docs/Glossary/Doctype">MDN:
///     Doctype</see>
/// </summary>
public sealed class Doctype : Component;
