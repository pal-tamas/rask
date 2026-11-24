namespace Rask.Values;

/// <summary>The kind of text a value object declared from <see cref="Text" /> holds: which base it was declared from.</summary>
internal enum TextKind
{
    /// <summary><see cref="Text" /> itself: no format beyond its lengths and pattern.</summary>
    Plain,

    /// <summary><see cref="Email" />: checked as <c>[EmailAddress]</c> checks it.</summary>
    Email,

    /// <summary><see cref="Phone" />: checked as <c>[Phone]</c> checks it.</summary>
    Phone,

    /// <summary><see cref="Url" />: an absolute http or https address.</summary>
    Url,

    /// <summary><see cref="Password" />: only its lengths; never shown, stored or sent back.</summary>
    Password,

    /// <summary>A new <see cref="Password" /> (<c>New: true</c>): the one a reader chooses, not the one they sign in with.</summary>
    NewPassword,

    /// <summary><see cref="Multiline" />: a text of several lines, typed into a textarea.</summary>
    Multiline,
}
