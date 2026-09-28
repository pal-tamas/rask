using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>A type generated beside the island: an enum, a record, or a string-or-number union.</summary>
internal sealed record GeneratedType(
    string Kind,
    string Name,
    string Summary,
    EquatableArray<GeneratedEnumMember> EnumMembers,
    EquatableArray<GeneratedRecordMember> RecordMembers);
