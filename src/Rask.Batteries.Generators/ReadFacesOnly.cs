using System;
using Microsoft.CodeAnalysis;

namespace Rask.Batteries.Generators;

/// <summary>
///     Whether this compilation wants the <b>read faces</b> and nothing else — the
///     <c>RaskReadFacesOnly</c> MSBuild property.
/// </summary>
/// <remarks>
///     <para>
///         For a PACKAGE that declares aggregates of its own and maps them by hand: Rask.Auth.Api owns
///         <c>Session</c> and <c>Passkey</c> and maps them through <c>modelBuilder.AddRaskAuth()</c>, which
///         an application calls when it wants auth. Generating the usual write surface there would do two
///         things nobody asked for.
///     </para>
///     <para>
///         It would map those tables a second time, and it would map them into <b>every</b> application that
///         merely references the package — auth switched off included — because the registry's contribution
///         is registered by a <c>[ModuleInitializer]</c> that runs when the assembly loads, and
///         <c>ModelRegistry.Apply</c> maps every contribution it has. It would also ship a public
///         <c>PasskeyModel</c> and <c>Passkey.Create(model)</c>: a form-shaped way to mint a passkey,
///         beside the WebAuthn ceremony that exists to prevent exactly that.
///     </para>
///     <para>
///         So the property turns off the write-side generators and leaves the read faces, which map into
///         the read context and are what an application actually needs to query somebody else's table.
///     </para>
/// </remarks>
internal static class ReadFacesOnly
{
    private const string Property = "build_property.RaskReadFacesOnly";

    /// <summary>The flag as an incremental value, so no generator reads MSBuild state twice.</summary>
    internal static IncrementalValueProvider<bool> Of(IncrementalGeneratorInitializationContext context) =>
        context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
            options.GlobalOptions.TryGetValue(Property, out var value) &&
            value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
