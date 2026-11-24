using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Rask.Data;

/// <summary>
///     What each aggregate and entity is asked before a save writes it: the validation attributes on its properties,
///     the attributes on its type, and its own <c>Validate()</c> and <c>ValidateDelete()</c>.
/// </summary>
/// <remarks>
///     <para>
///         Filled by a generated <c>[ModuleInitializer]</c> per assembly, so nothing is found by reflection and the
///         trimmer cannot remove a rule. Not an API to write against: declare the attribute or the method, and every
///         save asks it (<see cref="DomainValidation" />).
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedDomainRules
{
    private static readonly ConcurrentDictionary<Type, DomainRules> ByType = new();

    // Read on every save: a process whose entities declare nothing pays one field read for this whole feature.
    private static volatile bool _any;

    /// <summary>Whether any entity in the process has a rule to ask.</summary>
    internal static bool Any => _any;

    /// <summary>Registers what <typeparamref name="TEntity" /> is asked before a save writes it.</summary>
    /// <typeparam name="TEntity">The aggregate or entity.</typeparam>
    /// <param name="properties">Each property that carries a validation attribute.</param>
    /// <param name="typeChecks">The validation attributes on the type itself.</param>
    /// <param name="validate">Its <c>Validate()</c>, or <see langword="null" /> when it declares none.</param>
    /// <param name="validateDelete">Its <c>ValidateDelete()</c>, or <see langword="null" /> when it declares none.</param>
    /// <param name="words">
    ///     How a built-in attribute's failure is worded in the reader's language — Rask.Core's catalog, where the app
    ///     has it — or <see langword="null" /> for .NET's own text: what the generated <c>Create(model)</c> words it with.
    /// </param>
    public static void Add<TEntity>(
        IReadOnlyList<CheckedProperty> properties,
        IReadOnlyList<ValidationAttribute> typeChecks,
        Func<TEntity, IAsyncEnumerable<Problem>>? validate,
        Func<TEntity, IAsyncEnumerable<Problem>>? validateDelete,
        Func<ValidationAttribute, object?, string, string>? words)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(typeChecks);

        ByType[typeof(TEntity)] = new DomainRules(
            properties,
            typeChecks,
            validate is null ? null : entity => validate((TEntity)entity),
            validateDelete is null ? null : entity => validateDelete((TEntity)entity),
            words);
        _any = true;
    }

    /// <summary>What <paramref name="type" /> is asked, or null when it declares nothing.</summary>
    internal static DomainRules? Of(Type type) => ByType.GetValueOrDefault(type);
}
