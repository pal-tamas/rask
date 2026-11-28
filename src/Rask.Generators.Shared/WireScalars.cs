using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>
///     The scalars a Rask wire carries bare, each with the <c>Rask.Wire.WireJson</c> call that reads it (it takes
///     <c>(ref reader, property)</c>) and the write that puts it (<c>{0}</c> is the value). One table for the codecs
///     (<c>WireShape</c>) and for a value object's System.Text.Json converter (<see cref="ValueObjectJson" />), so the two
///     write a value alike.
/// </summary>
internal static class WireScalars
{
    private static readonly Dictionary<SpecialType, (string Read, string Write)> Scalars = new()
    {
        [SpecialType.System_Boolean] = ("global::Rask.Wire.WireJson.ReadBoolean", "writer.WriteBooleanValue({0})"),
        [SpecialType.System_Byte] = ("global::Rask.Wire.WireJson.ReadByte", "writer.WriteNumberValue({0})"),
        [SpecialType.System_SByte] = ("global::Rask.Wire.WireJson.ReadSByte", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int16] = ("global::Rask.Wire.WireJson.ReadInt16", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt16] = ("global::Rask.Wire.WireJson.ReadUInt16", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int32] = ("global::Rask.Wire.WireJson.ReadInt32", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt32] = ("global::Rask.Wire.WireJson.ReadUInt32", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Int64] = ("global::Rask.Wire.WireJson.ReadInt64", "writer.WriteNumberValue({0})"),
        [SpecialType.System_UInt64] = ("global::Rask.Wire.WireJson.ReadUInt64", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Single] = ("global::Rask.Wire.WireJson.ReadSingle", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Double] = ("global::Rask.Wire.WireJson.ReadDouble", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Decimal] = ("global::Rask.Wire.WireJson.ReadDecimal", "writer.WriteNumberValue({0})"),
        [SpecialType.System_Char] = ("global::Rask.Wire.WireJson.ReadChar", "global::Rask.Wire.WireJson.WriteCharValue(writer, {0})"),
        [SpecialType.System_String] = ("global::Rask.Wire.WireJson.ReadString", "writer.WriteStringValue({0})"),
        [SpecialType.System_DateTime] = ("global::Rask.Wire.WireJson.ReadDateTime", "writer.WriteStringValue({0})"),
    };

    private static readonly Dictionary<string, (string Read, string Write)> NamedScalars = new(StringComparer.Ordinal)
    {
        ["global::System.Guid"] = ("global::Rask.Wire.WireJson.ReadGuid", "writer.WriteStringValue({0})"),
        ["global::System.DateTimeOffset"] = ("global::Rask.Wire.WireJson.ReadDateTimeOffset", "writer.WriteStringValue({0})"),
        ["global::System.DateOnly"] = ("global::Rask.Wire.WireJson.ReadDateOnly", "global::Rask.Wire.WireJson.WriteDateOnlyValue(writer, {0})"),
        ["global::System.TimeOnly"] = ("global::Rask.Wire.WireJson.ReadTimeOnly", "global::Rask.Wire.WireJson.WriteTimeOnlyValue(writer, {0})"),
        ["global::System.TimeSpan"] = ("global::Rask.Wire.WireJson.ReadTimeSpan", "global::Rask.Wire.WireJson.WriteTimeSpanValue(writer, {0})"),
        ["global::System.Uri"] = ("global::Rask.Wire.WireJson.ReadUri", "global::Rask.Wire.WireJson.WriteUriValue(writer, {0})"),
    };

    /// <summary>The read and the write of <paramref name="type" />, or null when the wire carries it as no scalar.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The read call and the write's format, or null.</returns>
    public static (string Read, string Write)? Of(ITypeSymbol type)
    {
        if (Scalars.TryGetValue(type.SpecialType, out var special))
        {
            return special;
        }

        return NamedScalars.TryGetValue(type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out var named)
            ? named
            : null;
    }
}
