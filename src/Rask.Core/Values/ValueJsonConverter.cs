using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rask.Values;

/// <summary>
///     How System.Text.Json writes and reads a value object of one value — <c>record DriverName(string Value) : Text(Value)</c>,
///     <c>record Litres(decimal Value) : Number(Value)</c>, <c>record Rank(int Value) : Value</c>: as the value it holds,
///     <c>"Ada"</c> or <c>12.5</c>, as Rask's own wires carry it. The value object generator puts it on each such record,
///     through a nested subclass that reads and makes it; not an API to write against.
/// </summary>
/// <remarks>
///     <para>
///         What is read is made through the record's constructor, so its rules are asked: a value they refuse is refused
///         here too, as it was when the record was read as an object.
///     </para>
///     <para>
///         The object a value object was written as before — <c>{"Value":"Ada"}</c>, its member's name in any casing — is
///         read too, so what was stored before this form still reads.
///     </para>
/// </remarks>
/// <typeparam name="TValue">The value object.</typeparam>
/// <typeparam name="THeld">The value it holds.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ValueJsonConverter<TValue, THeld> : JsonConverter<TValue>
    where TValue : Value
{
    private readonly string _member;

    /// <summary>Names the member the value is held in, which the older object form wrote it under.</summary>
    /// <param name="member">The member's name: <c>Value</c> for a record declared from a base that holds it.</param>
    protected ValueJsonConverter(string member) => _member = member;

    /// <inheritdoc />
    public sealed override TValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        var held = reader.TokenType == JsonTokenType.StartObject
            ? FromObject(ref reader, typeToConvert.Name)
            : ReadHeld(ref reader, typeToConvert.Name);
        return Make(held);
    }

    /// <inheritdoc />
    public sealed override void Write(Utf8JsonWriter writer, TValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        WriteHeld(writer, value);
    }

    /// <summary>Reads the held value, bare, off <paramref name="reader" />.</summary>
    /// <param name="reader">The reader, on the value.</param>
    /// <param name="property">What is being read, for an error.</param>
    /// <returns>The held value.</returns>
    protected abstract THeld ReadHeld(ref Utf8JsonReader reader, string property);

    /// <summary>Writes the value <paramref name="value" /> holds, bare.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The value object.</param>
    protected abstract void WriteHeld(Utf8JsonWriter writer, TValue value);

    /// <summary>The value object, made through its constructor from what was read.</summary>
    /// <param name="held">What was read.</param>
    /// <returns>The value object.</returns>
    protected abstract TValue Make(THeld held);

    // The object form it was written as before: its one member, under its name in any casing; the rest skipped.
    private THeld FromObject(ref Utf8JsonReader reader, string property)
    {
        var found = false;
        THeld held = default!;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isMember = string.Equals(reader.GetString(), _member, StringComparison.OrdinalIgnoreCase);
            reader.Read();
            if (isMember)
            {
                held = ReadHeld(ref reader, property);
                found = true;
            }
            else
            {
                reader.Skip();
            }
        }

        return found ? held : throw new JsonException($"'{property}' holds no '{_member}'.");
    }
}
