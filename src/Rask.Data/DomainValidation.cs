using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Rask.Cqrs;
using Rask.Wire;

namespace Rask.Data;

/// <summary>
///     The save's one domain step: before anything is written, every rule the aggregates and entities of the save
///     declare (<see cref="GeneratedDomainRules" />) is asked, and the save is refused with one
///     <see cref="RaskValidationException" /> naming each failure under its field.
/// </summary>
/// <remarks>
///     <para>
///         For each row the save adds or changes, in this order: the validation attributes of its properties — of
///         every property on a new row, but of an existing row only the properties this save changes, so an old row
///         that a rule tightened since can still be opened and saved for another field — then its type's attributes
///         and its <see cref="IValidatableObject" /> (or <see cref="IAsyncValidatableObject" />), then its
///         <c>Validate()</c>. The root of an aggregate is asked whenever anything in it changes, a child removed
///         included, because a rule over the rows is the root's. An aggregate the save deletes is asked its
///         <c>ValidateDelete()</c> instead. Last, each declared lock (<see cref="LockedByBuilderExtensions" />) of a row
///         the save writes, or of an aggregate whose child it writes, asks the store for the row's old and new parent.
///     </para>
///     <para>
///         The failures are named as the aggregate's form model names its fields — <c>Title</c>, <c>Lines[2].Quantity</c>
///         — and worded as its generated <c>Create(model)</c> words them, so a form shows them under the same fields.
///         A rule resolves services from the work in progress (<c>validationContext.GetService&lt;T&gt;()</c>).
///     </para>
/// </remarks>
internal static class DomainValidation
{
    // What a rule that said nothing of its own is shown as: the words Rask.Core's forms use.
    private const string Invalid = "Invalid value.";

    /// <summary>Asks every rule of the save <paramref name="context" /> is about to run.</summary>
    /// <param name="context">The context being saved.</param>
    /// <param name="cancellationToken">Cancels the rules in flight.</param>
    /// <exception cref="RaskValidationException">A rule refused what the save would write.</exception>
    internal static async Task CheckAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (!GeneratedDomainRules.Any && !ParentLocks.Any)
        {
            return;
        }

        var changed = context.ChangeTracker.Entries()
            .Where(static e => e.Entity is IEntity && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (changed.Count == 0)
        {
            return;
        }

        using var principals = TrackedPrincipals.During(context);
        var save = new Save(context, Ambient.Services, cancellationToken);
        foreach (var entry in changed)
        {
            await save.Row(entry).ConfigureAwait(false);
        }

        await save.Roots().ConfigureAwait(false);
        await save.Locks(changed).ConfigureAwait(false);

        if (save.Failures.Count > 0)
        {
            throw new RaskValidationException(save.Failures);
        }
    }

    private sealed class Save(DbContext context, IServiceProvider? services, CancellationToken cancellationToken)
    {
        // Each root the save touches, once, in the order it was first touched.
        private readonly List<EntityEntry> _roots = [];
        private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, RowPaths> _paths = new(ReferenceEqualityComparer.Instance);

        internal List<FieldFailure> Failures { get; } = [];

        internal async Task Row(EntityEntry entry)
        {
            var root = entry.Entity is IAggregate ? entry : RootOf(entry);
            if (root is not null && _seen.Add(root.Entity))
            {
                _roots.Add(root);
            }

            var rules = GeneratedDomainRules.Of(entry.Entity.GetType());
            if (entry.State == EntityState.Deleted || rules is null)
            {
                return;
            }

            var path = root is null || ReferenceEquals(root.Entity, entry.Entity) ? "" : PathOf(root, entry.Entity);
            await Properties(entry, rules, path).ConfigureAwait(false);

            // A root's own rules are asked once, below, however many of its rows changed; a child's are its own.
            if (root is null || !ReferenceEquals(root.Entity, entry.Entity))
            {
                await Whole(entry.Entity, rules, path).ConfigureAwait(false);
            }
        }

        internal async Task Roots()
        {
            foreach (var root in _roots)
            {
                if (GeneratedDomainRules.Of(root.Entity.GetType()) is not { } rules)
                {
                    continue;
                }

                if (root.State == EntityState.Deleted)
                {
                    if (rules.ValidateDelete is { } validateDelete)
                    {
                        await Told(validateDelete(root.Entity), "").ConfigureAwait(false);
                    }
                }
                else
                {
                    await Whole(root.Entity, rules, "").ConfigureAwait(false);
                }
            }
        }

        // The declared locks (LockedBy) of each row the save writes, and of each aggregate a write to a child of it
        // touches: its parent's state is asked of the store, one query per lock for all the rows of the save.
        internal async Task Locks(List<EntityEntry> changed)
        {
            if (!ParentLocks.Any)
            {
                return;
            }

            var written = new HashSet<object>(changed.Select(static e => e.Entity), ReferenceEqualityComparer.Instance);
            var rows = changed.Concat(_roots.Where(r => !written.Contains(r.Entity)));
            foreach (var type in rows.GroupBy(static e => e.Metadata.ClrType))
            {
                var locks = ParentLocks.Of(type.Key);
                if (locks.IsEmpty)
                {
                    continue;
                }

                var writes = type.Select(e => new LockTarget(e, PlaceOf(e))).ToList();
                foreach (var rule in locks)
                {
                    Failures.AddRange(await rule.Check(context, writes, cancellationToken).ConfigureAwait(false));
                }
            }
        }

        // Where a row sits in its aggregate's form; a deleted row holds no place, nor a root.
        private string PlaceOf(EntityEntry entry) =>
            entry.State == EntityState.Deleted || entry.Entity is IAggregate || RootOf(entry) is not { } root
                ? ""
                : PathOf(root, entry.Entity);

        // Up its foreign key; a row a save let go is a detached copy whose parent key nobody kept, so its root says.
        private EntityEntry? RootOf(EntityEntry entry) =>
            AuditingInterceptor.RootOf(context, entry) ??
            (ChildWrites.RootThatLetGo(entry.Entity) is { } root ? context.Entry(root) : null);

        private string PathOf(EntityEntry root, object row)
        {
            if (!_paths.TryGetValue(root.Entity, out var paths))
            {
                _paths[root.Entity] = paths = RowPaths.Of(root.Metadata, root.Entity);
            }

            return paths.Of(row);
        }

        // Every property of a new row; of a stored one, only what this save changes.
        private async Task Properties(EntityEntry entry, DomainRules rules, string path)
        {
            foreach (var property in rules.Properties)
            {
                if (MemberOf(entry, property.Name) is not { } member ||
                    (entry.State == EntityState.Modified && !member.IsModified))
                {
                    continue;
                }

                var value = property.ValueOf is { } unwrap ? unwrap(member.CurrentValue) : member.CurrentValue;
                var validation = new ValidationContext(entry.Entity, property.DisplayName(), services, null) { MemberName = property.Name };
                var failed = false;

                foreach (var check in property.Checks.Where(static c => c is not AsyncValidationAttribute))
                {
                    failed |= Add(rules, check, check.GetValidationResult(value, validation), value, path + property.Name);
                }

                // As .NET's asynchronous Validator: an awaited rule is asked only of a value the others let through.
                if (failed)
                {
                    continue;
                }

                foreach (var check in property.Checks.OfType<AsyncValidationAttribute>())
                {
                    var result = await check.GetValidationResultAsync(value, validation, cancellationToken).ConfigureAwait(false);
                    if (Add(rules, check, result, value, path + property.Name))
                    {
                        break;
                    }
                }
            }
        }

        // The rules about the row as a whole: its type's attributes, its IValidatableObject, then its Validate().
        private async Task Whole(object entity, DomainRules rules, string path)
        {
            var validation = new ValidationContext(entity, entity.GetType().Name, services, null);

            foreach (var check in rules.TypeChecks)
            {
                var result = check is AsyncValidationAttribute awaited
                    ? await awaited.GetValidationResultAsync(entity, validation, cancellationToken).ConfigureAwait(false)
                    : check.GetValidationResult(entity, validation);
                if (result is not null && result != ValidationResult.Success)
                {
                    Told(result, rules.Worded(check, entity, result.ErrorMessage ?? Invalid), path);
                }
            }

            switch (entity)
            {
                case IAsyncValidatableObject awaited:
                    await foreach (var result in awaited.ValidateAsync(validation, cancellationToken).ConfigureAwait(false))
                    {
                        Told(result, result?.ErrorMessage, path);
                    }

                    break;
                case IValidatableObject validatable:
                    foreach (var result in validatable.Validate(validation) ?? [])
                    {
                        Told(result, result?.ErrorMessage, path);
                    }

                    break;
            }

            if (rules.Validate is { } validate)
            {
                await Told(validate(entity), path).ConfigureAwait(false);
            }
        }

        private bool Add(DomainRules rules, ValidationAttribute check, ValidationResult? result, object? value, string field)
        {
            if (result is null || result == ValidationResult.Success)
            {
                return false;
            }

            var said = result.ErrorMessage ?? Invalid;
            Failures.Add(new FieldFailure(rules.Worded(check, value, said), [field]));
            return true;
        }

        // ValidationResult.Success is null, and an object is free to yield it for "this one is fine".
        private void Told(ValidationResult? result, string? message, string path)
        {
            if (result is null || result == ValidationResult.Success)
            {
                return;
            }

            var members = result.MemberNames.Where(static m => !string.IsNullOrEmpty(m)).Select(m => path + m).ToList();
            Failures.Add(new FieldFailure(message ?? Invalid, members.Count > 0 ? members : Row(path)));
        }

        private async Task Told(IAsyncEnumerable<Problem> problems, string path)
        {
            await foreach (var problem in problems.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                Failures.Add(new FieldFailure(problem.Message, problem.Field is { } field ? [path + field] : Row(path)));
            }
        }

        // A failure about a row as a whole is shown under the row; one about the root, under no field.
        private static string[] Row(string path) => path.Length == 0 ? [] : [path.TrimEnd('.')];

        // What EF Core holds now, not a getter (a property may be read through a private one); null for a property
        // that is not stored, which no save writes and so none is asked about.
        private static MemberEntry? MemberOf(EntityEntry entry, string name) =>
            entry.Metadata.FindProperty(name) is not null ||
            entry.Metadata.FindComplexProperty(name) is not null ||
            entry.Metadata.FindNavigation(name) is not null
                ? entry.Member(name)
                : null;
    }
}
