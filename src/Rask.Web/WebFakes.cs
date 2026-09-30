using System.Collections.Immutable;
using System.Linq.Expressions;
using Rask.Core;

namespace Rask.Web;

// The fakes standing in for web objects in the current test's flow. A chain asks here first: if a fake's path starts
// the chain's, the fake answers it and nothing reaches the browser. Flow-scoped, as Clock.Fake is, so tests running in
// parallel never see each other's fakes.
internal static class WebFakes
{
    private static readonly AsyncLocal<ImmutableList<Entry>?> Current = new();

    internal static bool Any => Current.Value is { IsEmpty: false };

    internal static Entry Add(IReadOnlyList<JsChain.Step> path)
    {
        var entry = new Entry(path);
        Current.Value = (Current.Value ?? ImmutableList<Entry>.Empty).Add(entry);
        return entry;
    }

    internal static void Remove(Entry entry) => Current.Value = Current.Value?.Remove(entry);

    // The innermost fake whose path starts `path`, and the steps after it.
    internal static bool Find(IReadOnlyList<JsChain.Step> path, out Entry entry, out IReadOnlyList<JsChain.Step> rest)
    {
        entry = null!;
        rest = [];
        foreach (var candidate in Current.Value ?? ImmutableList<Entry>.Empty)
        {
            if (candidate.Path.Count <= path.Count && (entry is null || candidate.Path.Count > entry.Path.Count)
                && candidate.Path.Select((s, i) => s.Matches(path[i])).All(m => m))
            {
                entry = candidate;
            }
        }

        if (entry is null)
        {
            return false;
        }

        rest = path.Skip(entry.Path.Count).ToList();
        return true;
    }

    // The member a Returns expression names, as a path of names from the faked object: c => c.ReadText() is readText.
    internal static string Member(Expression body)
    {
        var names = new List<string>();
        for (var e = body; e is not ParameterExpression;)
        {
            switch (e)
            {
                case MethodCallExpression call:
                    names.Add(call.Method.Name);
                    e = call.Object ?? throw new NotSupportedException("A faked member is named from the faked object: c => c.ReadText().");
                    break;
                case MemberExpression member:
                    names.Add(member.Member.Name);
                    e = member.Expression ?? throw new NotSupportedException("A faked member is named from the faked object: n => n.Language.");
                    break;
                default:
                    throw new NotSupportedException($"A faked member is a chain of the object's members, not {e.NodeType}.");
            }
        }

        names.Reverse();
        return string.Join(".", names);
    }

    // MDN's names, as the chain has them, for the steps after a fake's path.
    internal static string Member(IReadOnlyList<JsChain.Step> rest) => string.Join(".", rest.Select(s => s.Name));

    internal sealed class Entry(IReadOnlyList<JsChain.Step> path)
    {
        private readonly Lock _gate = new();
        private readonly List<WebCall> _calls = [];

        public IReadOnlyList<JsChain.Step> Path { get; } = path;

        // Keyed by member path, matched without case: the C# name a Returns expression uses (ReadText) is MDN's
        // (readText) with its first letter raised.
        public Dictionary<string, object?> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<Listener> Listeners { get; } = [];

        public IReadOnlyList<WebCall> Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.ToArray();
                }
            }
        }

        // A read, call or write the chain ends in: recorded (reads are not), and answered.
        public T Answer<T>(IReadOnlyList<JsChain.Step> rest)
        {
            var member = Member(rest);
            var last = rest.Count == 0 ? default : rest[^1];
            if (last.Kind == JsChain.StepWrite)
            {
                Record(member + "=", last.Args ?? []);
                Answers[member] = last.Args?.FirstOrDefault();
                return default!;
            }

            if (last.Kind == JsChain.StepCall)
            {
                Record(member, last.Args ?? []);
            }

            return Answers.TryGetValue(member, out var value) ? Cast<T>(member, value) : default!;
        }

        private void Record(string member, object?[] args)
        {
            lock (_gate)
            {
                _calls.Add(new WebCall(member, args));
            }
        }

        private static T Cast<T>(string member, object? value) => value switch
        {
            null => default!,
            T typed => typed,
            _ => throw new InvalidOperationException(
                $"The fake answers {member} with a {value.GetType().Name}, but the code under test reads it as a {typeof(T).Name}."),
        };
    }

    internal sealed record Listener(string Type, Component Owner, Func<object, Task> Invoke);
}
