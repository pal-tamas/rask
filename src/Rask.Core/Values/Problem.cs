using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Rask.Values;

/// <summary>
///     One rule a value object breaks, and the member it is about: what <see cref="Value" />'s <c>Rules()</c> yields.
/// </summary>
/// <param name="Field">
///     The member the problem is shown under — <c>"Quantity"</c>, <c>"End"</c> — or null for the value as a whole. The
///     generated <c>Create(…, field)</c> puts the field it is given in front: <c>"Km.End"</c>.
/// </param>
/// <param name="Message">What the person is told.</param>
public readonly record struct Problem(string? Field, string Message)
{
    /// <summary>A problem shown under the member it names: <c>Problem.At(Quantity, "Both or neither.")</c>.</summary>
    /// <typeparam name="T">The member's type.</typeparam>
    /// <param name="member">The member, written as it is read; only its name is kept.</param>
    /// <param name="message">What the person is told.</param>
    /// <param name="field">Filled in by the compiler from <paramref name="member" />; never passed.</param>
    /// <returns>The problem, under the member's name.</returns>
#pragma warning disable IDE0060, RCS1163 // `member` is read only for its CallerArgumentExpression: the name it is written as
    public static Problem At<T>(T member, string message, [CallerArgumentExpression(nameof(member))] string field = "") =>
        new(MemberName(field), message);
#pragma warning restore IDE0060, RCS1163

    /// <summary>
    ///     A problem shown under a member deep inside a list the value holds, its positions evaluated:
    ///     <c>Problem.At(() =&gt; Wells[i].Companies[j].DbId, "Not this partner's.")</c> is under
    ///     <c>Wells[2].Companies[0].DbId</c>, the path a form shows that row's field at.
    /// </summary>
    /// <typeparam name="T">The member's type.</typeparam>
    /// <param name="member">The member, as a chain of members and indexers; read only for its path.</param>
    /// <param name="message">What the person is told.</param>
    /// <returns>The problem, under that path.</returns>
    /// <exception cref="ArgumentException"><paramref name="member" /> is not a chain of members and indexers.</exception>
    public static Problem At<T>(Expression<Func<T>> member, string message)
    {
        ArgumentNullException.ThrowIfNull(member);
        return new(FieldPath.PathOf(member), message);
    }

    /// <summary>A problem about the value as a whole, shown under the field the value came from.</summary>
    /// <param name="message">What the person is told.</param>
    /// <returns>The problem, under no member.</returns>
    public static Problem Whole(string message) => new(null, message);

    // `this.End` and `End` are the same member; an empty expression names none.
    internal static string? MemberName(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var name = expression.Trim();
        return name.StartsWith("this.", StringComparison.Ordinal) ? name[5..] : name;
    }
}
