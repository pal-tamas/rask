using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Rask.Data;

/// <summary>
///     Stores a <see cref="Secret{T}" /> as the ciphertext of the text it holds, wherever it is held — a column of its
///     own, or a member of a value object inside a JSON column. Registered for each <c>Secret&lt;T&gt;</c> a model holds
///     by generated code.
/// </summary>
/// <typeparam name="T">What the secret holds: a <see cref="string" />, or a value object that holds one text.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SecretConverter<[DynamicallyAccessedMembers(SecretConverter<T>.Built)] T>()
    : ValueConverter<Secret<T>, string>(secret => Protect(secret), stored => Unprotect(stored))
    where T : class
{
    // What building the value back reads: its constructor taking the text, and the `Value` that hands it out.
    internal const DynamicallyAccessedMemberTypes Built =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties;

    private static readonly (Func<T, string> Text, Func<string, T> Build) Shape = ShapeOf();

    private static string Protect(Secret<T> secret) => SecretKeeping.Current.Protect(Shape.Text(secret.Reveal()));

    // A stored value loads past its rules, as every stored value does: a rule tightened since does not lose the key.
    private static Secret<T> Unprotect(string stored)
    {
        var text = SecretKeeping.Current.Unprotect(stored);
        using (RowLoading.Begin())
        {
            return Secret.Of(Shape.Build(text));
        }
    }

    private static (Func<T, string> Text, Func<string, T> Build) ShapeOf()
    {
        if (typeof(T) == typeof(string))
        {
            return (static value => (string)(object)value, static text => (T)(object)text);
        }

#pragma warning disable S3011 // a value object's constructor is private by design (RASK127); EF Core binds it the same way
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
#pragma warning restore S3011

        if (typeof(T).GetConstructor(Any, [typeof(string)]) is not { } constructor ||
            typeof(T).GetProperty("Value", typeof(string)) is not { GetMethod: { } read })
        {
            throw new InvalidOperationException(
                $"A Secret<{typeof(T).Name}> is stored as the text it holds, but '{typeof(T).Name}' holds no one text: " +
                "keep a string, or a value object declared from Text (or Password) — record ApiKey(string Value) : Text(Value).");
        }

        return (value => (string)read.Invoke(value, null)!, text => (T)constructor.Invoke([text]));
    }
}
