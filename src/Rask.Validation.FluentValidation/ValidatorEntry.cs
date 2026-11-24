using System.ComponentModel;
using FluentValidation;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     One validator the generator found for a type — an <c>AbstractValidator&lt;T&gt;</c> class, a type's own
///     <c>static void Rules(InlineValidator&lt;T&gt; rules)</c>, or a generated form model that borrows its aggregate's
///     rules — as <see cref="RaskValidators.Replace" /> is handed it. Made by <see cref="RaskValidators.Entry{T}" />,
///     <see cref="RaskValidators.Inline{T}" /> and <see cref="RaskValidators.Project{TModel, TOwner}" />.
/// </summary>
/// <remarks>Built by generated code. Not an API to write against.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ValidatorEntry
{
    internal ValidatorEntry(
        Type model,
        Func<IServiceProvider?, IValidator?> build,
        Func<IReadOnlyList<IValidator>, IValidator> combine,
        Func<IValidator, string, StatedFacets> facets,
        ModelProjection? projection = null)
    {
        Model = model;
        Build = build;
        Combine = combine;
        Facets = facets;
        Projection = projection;
    }

    /// <summary>The type the validator validates.</summary>
    internal Type Model { get; }

    /// <summary>Builds the validator, given the scope its dependencies come from; null when it has no rule to run.</summary>
    internal Func<IServiceProvider?, IValidator?> Build { get; }

    /// <summary>Runs several validators of the same type as one.</summary>
    internal Func<IReadOnlyList<IValidator>, IValidator> Combine { get; }

    /// <summary>What one built validator states about a member.</summary>
    internal Func<IValidator, string, StatedFacets> Facets { get; }

    /// <summary>For a generated form model: the aggregate whose rules it borrows, member by member.</summary>
    internal ModelProjection? Projection { get; }
}
