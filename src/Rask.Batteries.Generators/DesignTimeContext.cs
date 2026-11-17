namespace Rask.Batteries.Generators;

/// <summary>An application context <c>dotnet ef</c> gets a generated design-time factory for.</summary>
/// <param name="FullyQualifiedName">The context's name as generated code spells it — <c>global::Shop.ShopDb</c>.</param>
/// <param name="Namespace">The namespace the factory is declared in, beside the context; empty for the global one.</param>
/// <param name="DisplayName">The context's own name, as an error sentence says it — <c>ShopDb</c>.</param>
/// <param name="FactoryName">The generated factory's type name.</param>
internal sealed record DesignTimeContext(string FullyQualifiedName, string Namespace, string DisplayName, string FactoryName);
