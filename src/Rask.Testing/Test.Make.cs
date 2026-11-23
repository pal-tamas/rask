using System.Diagnostics.CodeAnalysis;
using Rask.Data;

namespace Rask.Testing;

public static partial class Test
{
    /// <summary>
    ///     A valid <typeparamref name="T" />, built from the rules it hands its base, for a test that needs one and does not
    ///     care which: <c>var vin = Test.Make&lt;Vin&gt;();</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The value is made through the generated <c>Create</c>, so its rules — the base's and its own <c>Rules()</c> —
    ///         accept it, and every call makes the same one. A <see cref="Text" /> is filled to its <c>MinLength</c> (or a
    ///         few letters, within its <c>MaxLength</c>), and a simple <c>Pattern</c> — literals, <c>\d</c>, <c>\w</c>,
    ///         character classes, groups and counted repeats, like <c>^[A-Z]{3}-\d{3}$</c> — is met; a <see cref="Number" />
    ///         or a <see cref="Whole" /> is its <c>Min</c>, or 1 within its <c>Max</c>, at its scale; a <see cref="Day" /> or a
    ///         <see cref="Moment" /> is its earliest, or <see cref="Clock.Today" /> (<see cref="Clock.Wall" />) within its latest; a <see cref="Rask.Data.Range" /> runs from its
    ///         <c>Min</c> (or 1, or today) one step on. A value object declared from <see cref="Value" /> gets a sample of
    ///         each member: text, a number, a day, an enum's first member, or another value object made the same way.
    ///     </para>
    ///     <para>
    ///         A test that cares about the value makes it itself, with <c>Vin.Create("…")</c>: a value object has no
    ///         <c>with</c> outside its type, so there is nothing to tweak a made one with.
    ///     </para>
    /// </remarks>
    /// <typeparam name="T">The value object: a <c>partial record</c> the Rask generator gave a <c>Create</c>.</typeparam>
    /// <returns>A <typeparamref name="T" /> its rules accept.</returns>
    /// <exception cref="InvalidOperationException">
    ///     <typeparamref name="T" /> has no generated <c>Create</c>, its pattern or members are beyond what a sample is
    ///     built for, or its own rules refuse the sample; the message names it and says to make it by hand.
    /// </exception>
    public static T Make<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>()
        where T : Value =>
        (T)ValueSample.Make(typeof(T));
}
