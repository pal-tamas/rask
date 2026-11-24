using System.Diagnostics;

namespace Rask.Values;

/// <summary>
///     The base of a value object that holds a password as it is typed:
///     <c>public sealed partial record SignInPassword(string Value) : Password(Value, MinLength: 8);</c>
/// </summary>
/// <remarks>
///     <para>
///         Input only. Its rules are its lengths, its pattern and its own <c>Rules()</c>; it is never trimmed unless
///         declared <c>Trim: true</c>, since a blank is part of a password. A control bound to it is an
///         <c>&lt;input type="password" autocomplete="current-password"&gt;</c>, or <c>autocomplete="new-password"</c>
///         for one declared <c>New: true</c> — the password a reader chooses rather than the one they sign in with.
///     </para>
///     <para>
///         It never shows what it holds: <see cref="Text.ToString" /> and the debugger say <c>"••••"</c>, so a log line
///         or an interpolation does not leak it. It is never stored either — an aggregate or an entity that holds one
///         is RASK173 (store its hash) — nor sent back: a query's or a command's result, or an event, that carries
///         one is RASK174. Read <see cref="Text.Value" /> where it is checked or hashed, and nowhere else.
///     </para>
/// </remarks>
[DebuggerDisplay("••••")]
[DebuggerTypeProxy(typeof(PasswordView))]
public abstract record Password : Text
{
    /// <summary>What a password shows in place of its text.</summary>
    internal const string Masked = "••••";

    /// <summary>Keeps <paramref name="Value" /> once the rules the other arguments declare accept it.</summary>
    /// <param name="Value">The password as typed; the derived record's own positional parameter.</param>
    /// <param name="MaxLength">The most characters it may hold; 0 for no limit.</param>
    /// <param name="MinLength">The fewest characters it must hold; 0 for none beyond being filled.</param>
    /// <param name="Pattern">A regular expression it must match, or null.</param>
    /// <param name="Message">What a password that does not match <paramref name="Pattern" /> is told, or null for the framework's.</param>
    /// <param name="Trim">Whether surrounding blanks are cut off: not unless declared, since they are part of it.</param>
    /// <param name="Upper">Whether it is kept in capitals.</param>
    /// <param name="Required">Whether it must be filled.</param>
    /// <param name="New">
    ///     Whether it is a password the reader chooses (sign-up, change) rather than the one they sign in with: its input
    ///     says <c>autocomplete="new-password"</c>, so a password manager offers to make one.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A length is negative, or the fewest exceed the most.</exception>
    /// <exception cref="Rask.Cqrs.RaskValidationException">A rule refuses the password.</exception>
#pragma warning disable IDE1006 // PascalCase on purpose: the declaration reads `Password(Value, MinLength: 8, New: true)`, like an attribute's named argument
    protected Password(
        string Value, int MaxLength = 0, int MinLength = 0, string? Pattern = null, string? Message = null, bool Trim = false, bool Upper = false,
        bool Required = true, bool New = false)
#pragma warning restore IDE1006
        : base(Value, new TextDeclared(MaxLength, MinLength, Pattern, Message, Trim, Upper, Required, New ? TextKind.NewPassword : TextKind.Password))
    {
    }

    private protected sealed override string Shown() => Masked;

    // What the debugger expands a password into: the mask, never the text.
    private sealed class PasswordView(Password password)
    {
        public string Value { get; } = password is null ? "" : Masked;
    }
}
