using System.Linq.Expressions;

namespace Rask.Data;

/// <summary>
///     A lambda a model declaration took — a computed column's value, an index's filter — kept on the model until the
///     provider is known and it is written as that provider's SQL.
/// </summary>
/// <param name="Lambda">The lambda, as the app wrote it.</param>
/// <param name="Declaration">The declaration, for a refusal to name: <c>Computed(x =&gt; x.Name, …)</c>.</param>
internal sealed record DeclaredExpression(LambdaExpression Lambda, string Declaration);
