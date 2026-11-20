using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Api.Generators;

/// <summary>
///     A diagnostic as a value: the descriptor, where, and its message arguments as text. What a transform reports
///     travels to the output step in this form, because a <see cref="Diagnostic" /> holds a <see cref="Location" />,
///     which is neither equatable nor safe to cache across compilations.
/// </summary>
internal sealed record ApiDiagnostic(DiagnosticDescriptor Descriptor, SymbolLocation? At, EquatableArray<string> Arguments)
    : IEquatable<ApiDiagnostic>
{
    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, At?.ToLocation() ?? Location.None, Arguments.Cast<object>().ToArray());
}
