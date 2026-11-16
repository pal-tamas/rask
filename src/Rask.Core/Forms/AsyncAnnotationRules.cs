using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Rask.Core.Forms;

// What a model type asks of the asynchronous half of DataAnnotations (.NET 11): an AsyncValidationAttribute on a
// property or on the type, or the type being an IAsyncValidatableObject. Read once per type; null — the common
// case, and the whole cost of it — for a type with none.
//
// Such a type is validated the way .NET's own asynchronous Validator does it: an async attribute is awaited
// (IsValidAsync) and never asked synchronously, and an IAsyncValidatableObject's ValidateAsync runs in place of its
// Validate. Its synchronous attributes still run first and still win: an async rule is asked about a value only
// once everything synchronous on that field has passed — a [Required] email is not looked up while it is empty.
internal sealed class AsyncAnnotationRules
{
    private static readonly ConcurrentDictionary<Type, AsyncAnnotationRules?> _byType = new();

    // Whether a model type's graph reaches a type with async rules. Asked on every render of a form, so cached.
    private static readonly ConcurrentDictionary<Type, bool> _reached = new();

    // The members by name: a field check finds its own in one lookup.
    private readonly Dictionary<string, Member> _byName;

    private AsyncAnnotationRules(Member[] members, ValidationAttribute[] typeChecks, bool asyncObject)
    {
        Members = members;
        _byName = members.ToDictionary(static m => m.Name, StringComparer.Ordinal);
        TypeChecks = typeChecks;
        AsyncObject = asyncObject;
    }

    /// <summary>Every property that carries an attribute, in declaration order, with its attributes split.</summary>
    internal Member[] Members { get; }

    /// <summary>The attributes on the type itself.</summary>
    internal ValidationAttribute[] TypeChecks { get; }

    /// <summary>Whether the type is an <see cref="IAsyncValidatableObject" />.</summary>
    internal bool AsyncObject { get; }

    /// <summary>The async rules of <paramref name="type" />, or null when it has none.</summary>
    internal static AsyncAnnotationRules? Of(Type type) => _byType.GetOrAdd(type, Read);

    /// <summary>
    ///     Whether <paramref name="model" />'s type, or a type reachable through its properties and collections, has
    ///     async rules: whether a form over it needs the asynchronous pass at all.
    /// </summary>
    internal static bool Reached(Type model) => _reached.GetOrAdd(model, Reach);

    /// <summary>Whether validating <paramref name="member" /> awaits anything here.</summary>
    internal bool Covers(string member) => AsyncObject || Find(member) is { Async.Length: > 0 };

    /// <summary>The member called <paramref name="name" />, or null when it carries no attribute.</summary>
    internal Member? Find(string name) => _byName.GetValueOrDefault(name);

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "GetProperties on a model type the app validates: the app preserves its models' public " +
                        "properties for validation, as for every DataAnnotations pass (DataAnnotationsFieldValidator).")]
    private static AsyncAnnotationRules? Read(Type type)
    {
        if (ModelGraphWalker.IsLeaf(type))
        {
            return null;
        }

        var members = new List<Member>();
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null)
            {
                continue;
            }

            var checks = property.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray();
            if (checks.Length > 0)
            {
                members.Add(new Member(property, checks));
            }
        }

        var typeChecks = type.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray();
        var asyncObject = typeof(IAsyncValidatableObject).IsAssignableFrom(type);
        var hasAsync = asyncObject
                       || members.Exists(static m => m.Async.Length > 0)
                       || Array.Exists(typeChecks, static check => check is AsyncValidationAttribute);
        return hasAsync ? new AsyncAnnotationRules([.. members], typeChecks, asyncObject) : null;
    }

    // Breadth-first over declared property types, collection items and dictionary values: what a form over the model
    // can reach. A property typed as a base class reaches that base's rules, not a subclass's.
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "GetProperties on a model type the app validates — see Read.")]
    private static bool Reach(Type model)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(model);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!seen.Add(type) || ModelGraphWalker.IsLeaf(type))
            {
                continue;
            }

            if (Of(type) is not null)
            {
                return true;
            }

            foreach (var next in Next(type))
            {
                queue.Enqueue(next);
            }
        }

        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "See Reach.")]
    private static IEnumerable<Type> Next(Type type)
    {
        if (Item(type) is { } item)
        {
            yield return item;
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                yield return Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            }
        }
    }

    // What a collection holds: a dictionary's values, a sequence's items. Null for anything else.
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "GetInterfaces on a model type — see Reach.")]
    private static Type? Item(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        Type? sequence = null;
        foreach (var face in type.IsInterface ? [type, .. type.GetInterfaces()] : type.GetInterfaces())
        {
            if (!face.IsGenericType)
            {
                continue;
            }

            var definition = face.GetGenericTypeDefinition();
            if (definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                return face.GetGenericArguments()[1];
            }

            if (definition == typeof(IEnumerable<>))
            {
                sequence ??= face.GetGenericArguments()[0];
            }
        }

        return sequence;
    }

    /// <summary>One property's attributes: <see cref="RequiredAttribute" /> first, as the Validator asks it.</summary>
    internal sealed class Member
    {
        internal Member(PropertyInfo property, ValidationAttribute[] checks)
        {
            Property = property;
            Required = checks.OfType<RequiredAttribute>().FirstOrDefault();
            Sync = [.. checks.Where(check => check is not AsyncValidationAttribute && !ReferenceEquals(check, Required))];
            Async = [.. checks.OfType<AsyncValidationAttribute>()];
            AllSync = [.. checks.Where(static check => check is not AsyncValidationAttribute)];
        }

        internal PropertyInfo Property { get; }

        internal string Name => Property.Name;

        internal RequiredAttribute? Required { get; }

        /// <summary>The synchronous attributes other than <see cref="Required" />.</summary>
        internal ValidationAttribute[] Sync { get; }

        /// <summary>Every synchronous attribute, <see cref="Required" /> included: what the synchronous pass asks.</summary>
        internal ValidationAttribute[] AllSync { get; }

        internal AsyncValidationAttribute[] Async { get; }
    }
}
