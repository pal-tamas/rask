using System;
using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>A member of a generated record.</summary>
internal sealed record GeneratedRecordMember(
    string ClrName,
    string Wire,
    CsType Type,
    bool Required,
    bool Nullable,
    string? Doc);
