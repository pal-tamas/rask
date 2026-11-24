namespace Rask.Core.Forms;

/// <summary>
///     What the rules a validation library states in code for one member say about its field and its column — what
///     the DataAnnotations attribute with the same rule would say: FluentValidation's <c>NotEmpty()</c> is
///     <c>[Required]</c>, its <c>MaximumLength(200)</c> is <c>[MaxLength(200)]</c>.
/// </summary>
/// <remarks>Only rules that always apply are read: one under a <c>When</c> or <c>Unless</c> states nothing here.</remarks>
/// <param name="Required">Whether the member must be filled: a required badge, <c>aria-required</c> and a NOT NULL column.</param>
/// <param name="MaxLength">The most characters it may hold: its <c>maxlength</c> and its column's length.</param>
/// <param name="MinLength">The fewest characters it may hold: its <c>minlength</c>.</param>
/// <param name="Min">The least it may be: its input's <c>min</c>.</param>
/// <param name="Max">The greatest it may be: its input's <c>max</c>.</param>
/// <param name="Email">Whether it is an e-mail address: an <c>type="email"</c> input.</param>
/// <param name="Precision">The most digits it may have: its column's precision.</param>
/// <param name="Scale">The most digits after the point it may have: its input's <c>step</c> and its column's scale.</param>
public sealed record StatedFacets(
    bool Required = false,
    int? MaxLength = null,
    int? MinLength = null,
    decimal? Min = null,
    decimal? Max = null,
    bool Email = false,
    int? Precision = null,
    int? Scale = null)
{
    /// <summary>Nothing stated.</summary>
    public static StatedFacets None { get; } = new();

    /// <summary>Whether these say nothing at all.</summary>
    public bool IsEmpty => this == None;

    /// <summary>What these and <paramref name="other" /> say together: the tightest of both where both say something.</summary>
    /// <param name="other">What another rule states about the same member.</param>
    /// <returns>Both, merged.</returns>
    public StatedFacets With(StatedFacets other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.IsEmpty)
        {
            return this;
        }

        return IsEmpty
            ? other
            : new StatedFacets(
                Required || other.Required,
                Bounds.Least(MaxLength, other.MaxLength),
                Bounds.Most(MinLength, other.MinLength),
                Bounds.Most(Min, other.Min),
                Bounds.Least(Max, other.Max),
                Email || other.Email,
                Bounds.Least(Precision, other.Precision),
                Bounds.Least(Scale, other.Scale));
    }
}
