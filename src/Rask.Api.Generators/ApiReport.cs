using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Api.Generators;

/// <summary>Collects what describing an endpoint reported, for the output step to report.</summary>
internal sealed class ApiReport
{
    private readonly List<ApiDiagnostic> _diagnostics = new();

    public EquatableArray<ApiDiagnostic> Diagnostics => new(_diagnostics);

    public void Add(DiagnosticDescriptor descriptor, Location? location, params object[] arguments) =>
        _diagnostics.Add(new ApiDiagnostic(
            descriptor,
            SymbolLocation.From(location),
            new EquatableArray<string>(arguments.Select(static a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? string.Empty))));
}
