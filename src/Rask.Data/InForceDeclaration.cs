namespace Rask.Data;

/// <summary>What <see cref="InForceBuilderExtensions.InForceWhile{TChild}" /> declared, until the index is made from it.</summary>
/// <param name="Rows">The rows in force.</param>
/// <param name="Message">What a second one is refused with.</param>
internal sealed record InForceDeclaration(DeclaredExpression Rows, string Message);
