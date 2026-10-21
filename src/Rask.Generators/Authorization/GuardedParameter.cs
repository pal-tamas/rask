namespace Rask.Generators.Authorization;

/// <summary>One parameter of a guarded method, as the interceptor and its accessor repeat it.</summary>
/// <param name="Type">The type as a signature writes it, nullability included.</param>
/// <param name="TypeOf">The type as <c>typeof</c> takes it, to find the method's <c>MethodInfo</c>.</param>
/// <param name="Modifier"><c>ref </c>, <c>out </c>, <c>in </c>, <c>params </c>, or empty.</param>
/// <param name="Name">The parameter's name, escaped.</param>
internal sealed record GuardedParameter(string Type, string TypeOf, string Modifier, string Name);
