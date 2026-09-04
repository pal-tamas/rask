using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.JSInterop;

namespace Rask.Blazor.Tests;

/// <summary>
///     Pins the two annotations that make a hosted component survive a trimmed publish.
/// </summary>
/// <remarks>
///     <para>
///         Both are invisible to every other gate in this repository. The trim analyser cannot report
///         the first, because the reflection it protects lives inside
///         <c>Microsoft.AspNetCore.Components</c> on a type this package does not own; and a
///         server-only test run never publishes trimmed at all. The failure they prevent is the worst
///         shape available: the island renders as an EMPTY element, with no warning at build, no
///         exception at runtime and nothing in the console.
///     </para>
///     <para>
///         The browser E2E over the WASM showcase's Blazor island page is what proves the mechanism
///         end to end. These tests are the fast half — they fail in the unit gate, in seconds, and say
///         what was removed and why it mattered.
///     </para>
/// </remarks>
public sealed class TrimmingContractTests
{
    /// <summary>
    ///     <c>TComponent</c> keeps its <see cref="DynamicallyAccessedMembersAttribute" />.
    /// </summary>
    /// <remarks>
    ///     <c>ParameterView.SetParameterProperties</c> assigns a hosted component's parameters by
    ///     reflecting over its public properties. Under <c>PublishTrimmed</c> — the default for a WASM
    ///     app — the trimmer removes the setters that nothing calls statically, and the component then
    ///     renders with every parameter at its default. Verified by removing this annotation and
    ///     watching the showcase's island render empty.
    /// </remarks>
    [Fact]
    public void The_hosted_type_parameter_keeps_its_properties_through_trimming()
    {
        var parameter = typeof(BlazorComponent<>).GetGenericArguments()[0];
        var annotation = parameter.GetCustomAttribute<DynamicallyAccessedMembersAttribute>();

        Assert.NotNull(annotation);
        Assert.True(
            annotation.MemberTypes.HasFlag(DynamicallyAccessedMemberTypes.PublicProperties),
            "BlazorComponent<TComponent> must keep PublicProperties on its type parameter, or a trimmed "
            + "WASM app renders every hosted component with its parameters unset — silently.");

        // All, and for a second reason on top of parameters (#956). The component is built through the
        // renderer's InstantiateComponent — the only path that runs [Inject] property injection — and
        // that method declares LinkerFlags.Component, which IS All. Narrower here is IL2087 at the call
        // site: a build error in every consuming WASM app, since those publish trimmed under
        // warnings-as-errors. And injection reflects over NON-PUBLIC [Inject] properties too, so a
        // narrower annotation would let the trimmer remove exactly what the activator is about to
        // assign — an island that renders perfectly with every injected service null.
        Assert.Equal(DynamicallyAccessedMemberTypes.All, annotation.MemberTypes);
    }

    /// <summary>
    ///     The <see cref="IJSRuntime" /> shim repeats the interface's own annotation.
    /// </summary>
    /// <remarks>
    ///     An implementation whose <c>DynamicallyAccessedMembers</c> differs from the member it
    ///     implements is IL2095 — which, in a WASM app publishing trimmed under warnings-as-errors, is
    ///     a BUILD ERROR in the consuming app, for a method that only ever throws. It cost the
    ///     showcase its publish before this was matched.
    /// </remarks>
    [Fact]
    public void The_JSRuntime_shim_matches_the_interfaces_trimming_annotation()
    {
        // Only what the shim DECLARES. IJSRuntime also carries default interface methods
        // (GetValueAsync, SetValueAsync, InvokeConstructorAsync) that it inherits rather than
        // implements, and an inherited method cannot disagree with itself.
        var implemented = typeof(RaskBlazorJSRuntime)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m.IsGenericMethodDefinition)
            .ToList();

        Assert.NotEmpty(implemented);

        foreach (var implementation in implemented)
        {
            var declared = typeof(IJSRuntime).GetMethods().Single(
                m => m.Name == implementation.Name
                     && m.IsGenericMethodDefinition
                     && m.GetParameters().Length == implementation.GetParameters().Length);

            Assert.Equal(Annotation(declared), Annotation(implementation));
        }

        return;

        static DynamicallyAccessedMemberTypes Annotation(MethodInfo method) =>
            method.GetGenericArguments()[0]
                .GetCustomAttribute<DynamicallyAccessedMembersAttribute>()
                ?.MemberTypes ?? DynamicallyAccessedMemberTypes.None;
    }
}
