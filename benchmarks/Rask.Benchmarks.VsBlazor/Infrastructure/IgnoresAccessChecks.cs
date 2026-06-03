namespace System.Runtime.CompilerServices;

/// <summary>
///     Runtime-honoured marker that lets this assembly call <c>internal</c> members of
///     the named target assembly. Used to invoke
///     <c>Microsoft.AspNetCore.Components.Server.Circuits.RenderBatchWriter</c> for the
///     Blazor-side bytes-on-wire comparison.
///     <para>
///         The CLR honours either this declaration or the (identically-named) runtime-
///         internal one; CS0436 from the duplicate is silenced in the csproj.
///     </para>
///     <para>
///         Note: declaring this attribute does NOT bypass the C# compiler's accessibility
///         checks. Calls into internal Blazor types still go through reflection
///         (see <c>BlazorBatchByteSizer</c>). The attribute is here so that, if a future
///         reflection-emit / source-generator path replaces the reflection calls, the
///         JIT will not throw <see cref="MemberAccessException"/> at runtime.
///     </para>
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal sealed class IgnoresAccessChecksToAttribute(string assemblyName) : Attribute
{
    public string AssemblyName { get; } = assemblyName;
}
